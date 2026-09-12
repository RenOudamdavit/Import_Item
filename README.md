# SAP Business One — Item Master Import (C# / .NET 8)

A production-oriented importer that loads item master data (`OITM` / the Service Layer `Items`
entity) into SAP Business One from a delimited file, with validation before the first call to SAP,
per-row error isolation, and a CSV audit report of exactly what landed.

It is built for the real shape of the job: a business hands over a spreadsheet, some rows are wrong,
the run must not stop because of them, and somebody has to answer "which items actually went in?".

---

## Why it is put together this way

| Decision | Reason |
| --- | --- |
| **Service Layer (REST/OData) as the primary transport** | No SAP client installation, works against SQL Server and HANA companies, and runs from Linux containers and CI as happily as from Windows. A DI API gateway is included for shops that must use the SAP client stack. |
| **Upsert by default** | Migrations get re-run. Create-then-update means a second run fixes the rows that failed the first time instead of failing on "item code already exists". |
| **Only supplied columns are written** | An empty cell means "leave what SAP has", so re-importing a narrow file cannot blank out data maintained inside SAP. `<NULL>` is the explicit way to clear a field. |
| **Per-row failure isolation** | One bad item group code fails one row, not ten thousand. Connection-level failures do abort, because continuing would only produce identical failures. |
| **Creates are not retried by default** | A `POST` that times out may already have been committed by SAP. Retrying risks a duplicate item; reporting the row and re-running in upsert mode is safe. Reads and `PATCH` are retried. |
| **Validation happens before SAP** | Type, length, enumeration and key problems are reported with the source line number and column name, at a cost of zero SAP round trips. |

---

## Layout

```
Import_Item.sln
src/
  SapB1.ItemImport.Core/           Domain, CSV reader, field catalog, validation, orchestration, reporting
  SapB1.ItemImport.ServiceLayer/   Service Layer gateway: login, session renewal, retries, JSON payloads
  SapB1.ItemImport.Cli/            sapb1-import-items command line tool
  SapB1.ItemImport.DiApi/          Optional Windows-only DI API gateway (not in the solution)
tests/
  SapB1.ItemImport.Tests/          xUnit tests for parsing, mapping, payloads and orchestration
sample-data/                       Example item file and mapping file
```

`Core` knows nothing about HTTP, and nothing about CSV beyond its own reader:
`ItemImportService` consumes parsed rows and talks to an `IItemGateway`. That is what lets the
Service Layer and DI API paths share the same validation, the same reports and the same tests.

There are **no NuGet dependencies** in the runtime projects — only the .NET 8 base class library.
Locked-down SAP servers rarely enjoy restoring packages, and the test project is the only place that
pulls anything (xUnit).

---

## Prerequisites

- .NET 8 SDK (or newer) to build; .NET 8 runtime to run.
- SAP Business One **Service Layer** reachable over HTTPS — port `50000` by default, so the root URL
  is normally `https://<sap-host>:50000/b1s/v1`.
- A SAP Business One user with permission to create and update items, and a free licence for it.

Verify the endpoint before blaming the tool:

```bash
curl -k https://sap-host:50000/b1s/v1/$metadata | head
```

---

## Build and test

```bash
dotnet build Import_Item.sln
dotnet test  Import_Item.sln
```

---

## Quick start

1. Set the credentials (the password is never read from an argument):

   ```bash
   export SAPB1_URL="https://sap-host:50000/b1s/v1"
   export SAPB1_COMPANY="SBODEMOGB"
   export SAPB1_USER="manager"
   export SAPB1_PASSWORD='...'
   ```

2. Check the file without touching SAP:

   ```bash
   dotnet run --project src/SapB1.ItemImport.Cli -- \
     --file sample-data/items.csv --dry-run
   ```

3. Run it for real:

   ```bash
   dotnet run --project src/SapB1.ItemImport.Cli -- \
     --file sample-data/items.csv --report out/report.csv
   ```

`--dry-run` still checks which items exist, so the dry-run report tells you exactly how many creates
and updates to expect.

Run `sapb1-import-items --help` for every option, and `--show-fields` for the columns it understands.

---

## Source file format

The first row is the header. Delimiter (`,` `;` tab `|`) is auto-detected, a byte-order mark is
honoured, quoted fields may contain delimiters and newlines, and blank lines are skipped.

Column names are matched **ignoring case, spaces, underscores and hyphens**, so `ItemCode`,
`Item Code`, `item_code` and `ITEM-CODE` are the same column. Common business names are accepted as
aliases: `Description`, `SKU`, `Warehouse`, `Preferred Vendor`, `Min Stock`, `Active`, `Remarks`,
`Sales Tax Code`, and others.

```csv
ItemCode,ItemName,Item Group,Inventory Item,Warehouse,Preferred Vendor,Price,Currency,U_Category
A-1001,"Steel bracket, 40mm",100,Y,01,V10000,12.50,USD,Hardware
```

- **Item code** is required, at most 50 characters, and must be unique within the file.
- **Yes/No fields** accept `Y`, `N`, `Yes`, `No`, `True`, `False`, `1`, `0`, `tYES`, `tNO`.
- **Item type** accepts `item`, `labour`/`service`, `travel`, `fixed asset`.
- **Numbers** parse with the invariant culture unless `--culture de-DE` (or similar) is given, which
  switches to comma decimals.
- **Prices** use `PriceList` / `Price` / `Currency`, plus numbered variants `PriceList2` / `Price2` /
  `Currency2` for further price lists. A bare `Price` column uses `--default-price-list` (default 1).
- **User-defined fields**: any column named `U_<FieldName>` is sent through unchanged.
- **Empty cell** = leave the SAP value alone. **`<NULL>`** = clear the field.

Unrecognised columns produce a warning and are ignored; `--unknown-columns error` makes them fatal,
which is what you want for a controlled go-live.

### Mapping a file you do not control

```json
{
  "Columns": {
    "Artikelnummer": "ItemCode",
    "Artikelbezeichnung": "ItemName",
    "Warengruppe": "ItemsGroupCode",
    "Verkaufspreis": "Price",
    "Kostenstelle": null
  }
}
```

```bash
sapb1-import-items --file artikel.csv --mapping mapping.json --delimiter semicolon --culture de-DE
```

`null` drops a column deliberately and silences its warning. A mapping target may be any SAP `Items`
property — the built-in catalog covers the item-master fields needed for a normal go-live, and
anything else is passed straight through under the name you give it.

> Item master properties differ slightly between SAP versions and localisations. If SAP rejects a
> property name, check it against your own server: `GET /b1s/v1/$metadata`, then map the column to
> the exact name your version uses. No code change is needed.

---

## Modes

| Mode | Behaviour |
| --- | --- |
| `upsert` *(default)* | Create items that do not exist, update those that do. Safe to re-run. |
| `create-only` | Create only. Existing items are **skipped**, protecting data maintained in SAP. |
| `update-only` | Update only. Unknown item codes are reported as `NotFound` rather than created. |

---

## The report

Every processed row produces one line in the report CSV:

```csv
Line,ItemCode,Status,SapErrorCode,Detail
2,"A-1001","Created",,
3,"A-1002","SapRejected","-1116","SAP refused to create the item [SAP -1116]: Item group does not exist"
4,"A-1003","ValidationFailed",,"Item Group: 'ABC' is not a whole number."
```

`Status` is one of `Created`, `Updated`, `Skipped`, `ValidationFailed`, `SapRejected`, `NotFound`.
Results are streamed as they happen, so an interrupted run still leaves a usable report. Values are
quoted and formula-neutralised, so an error message beginning with `=` cannot execute when the report
is opened in Excel.

### Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Everything processed, nothing failed |
| `1` | Whole file processed, some rows failed — see the report |
| `2` | Stopped early: error limit, cancellation, or SAP became unreachable |
| `3` | Bad arguments, bad configuration or an unusable file — nothing was sent to SAP |

Ctrl+C cancels cooperatively: the current item finishes and the report is closed cleanly.

---

## Configuration

Precedence is **command line → environment → `appsettings.json` → defaults**.

| Environment variable | Purpose |
| --- | --- |
| `SAPB1_URL` | Service Layer root |
| `SAPB1_COMPANY` | Company database |
| `SAPB1_USER` | SAP user name |
| `SAPB1_PASSWORD` | SAP password |
| `SAPB1_CERT_THUMBPRINT` | Expected server certificate thumbprint |
| `SAPB1_TRUST_ANY_CERT` | Disable TLS validation (see below) |

See `src/SapB1.ItemImport.Cli/appsettings.json` for every file setting, each commented.

---

## Security notes

- **Passwords are never accepted as an argument.** `--password` is rejected with an explanation:
  arguments are visible to every user in the process list and are written to shell history. Use
  `SAPB1_PASSWORD` or the configuration file. Credentials are never logged.
- **TLS.** A default on-premise Service Layer ships a self-signed certificate. The right fixes, in
  order: install the SAP certificate in the machine trust store; or pin it with
  `--certificate-thumbprint <sha1>`. `--trust-any-certificate` exists for a lab and logs a loud
  warning — it accepts *any* certificate, including an attacker's.
- **No hand-built SQL.** The Service Layer path uses OData with properly escaped string literals; the
  DI API path uses `GetByKey` rather than a `Recordset` query, so item codes never reach a statement.
- **Report output is formula-neutralised** against CSV-injection via spreadsheet formulas.
- `.gitignore` excludes `appsettings.Local.json`, `.env`, `secrets.json` and report output.

---

## Optional: the DI API gateway (Windows only)

`src/SapB1.ItemImport.DiApi` implements the same `IItemGateway` over `SAPbobsCOM`, for installations
where the Service Layer is not available or not permitted. It is **deliberately excluded from
`Import_Item.sln`**, because building it requires the SAP DI API to be installed locally — which a
Linux or CI agent will not have.

```powershell
dotnet build src\SapB1.ItemImport.DiApi\SapB1.ItemImport.DiApi.csproj `
  -p:SapInteropPath="C:\Program Files\SAP\SAP Business One DI API\DI API 90\Interop.SAPbobsCOM.dll"
```

Requirements and caveats:

- Windows, x64, and a DI API version and bitness matching the SAP installation.
- The DI API is synchronous and COM-apartment-bound, so calls complete on the calling thread.
- Values are shared with the Service Layer path unchanged: SAP enumeration member names such as
  `tYES` and `itItems` are resolved onto the COM enums by name.
- Price lines must already exist in the company (the DI API addresses them by index), and clearing a
  non-string field is not supported — use the Service Layer for that.

To use it, construct `DiApiItemGateway` in place of `ServiceLayerItemGateway`; nothing else changes.

---

## Extending it

- **A new SAP field** — add one entry to `ItemFieldCatalog`, or just name the column after the SAP
  property and let it pass through.
- **A new source format** (Excel, a staging table) — produce `IEnumerable<ItemRecordParseResult>` and
  hand it to `ItemImportService`. Nothing else changes.
- **A different SAP object** (business partners, price lists) — implement a gateway for it; the
  batching, error isolation, reporting and exit-code behaviour are all reusable.
