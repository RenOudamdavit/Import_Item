using System.Diagnostics.CodeAnalysis;

namespace SapB1.ItemImport.Core.Mapping;

/// <summary>
/// The set of SAP Business One <c>Items</c> properties this importer understands, plus the
/// human-friendly column aliases operators actually put in spreadsheets.
/// </summary>
/// <remarks>
/// The catalog is deliberately conservative: it covers the item-master fields needed for a normal
/// go-live and leaves everything else to pass-through columns. Anything not listed here can still be
/// imported by naming the column after the SAP property (or by adding a mapping override), which
/// keeps this file from becoming a second, stale copy of the SAP schema.
/// </remarks>
public static class ItemFieldCatalog
{
    /// <summary>SAP property name of the item primary key.</summary>
    public const string ItemCodeProperty = "ItemCode";

    /// <summary>Prefix that marks a user-defined field (UDF) on the item master.</summary>
    public const string UserFieldPrefix = "U_";

    private static readonly IReadOnlyDictionary<string, string> YesNoValues = BuildYesNoValues();

    private static readonly ItemFieldDefinition[] Definitions =
    {
        new(ItemCodeProperty, SapFieldKind.Text, 50, description: "Item primary key (OITM.ItemCode)."),
        new("ItemName", SapFieldKind.Text, 100, description: "Item description."),
        new("ForeignName", SapFieldKind.Text, 100, description: "Foreign-language description."),
        new("ItemsGroupCode", SapFieldKind.Integer, description: "Item group number (OITB.ItmsGrpCod)."),
        new("ItemType", SapFieldKind.Enumeration, enumerationValues: BuildItemTypeValues(), description: "Item / labour / travel / fixed asset."),
        new("BarCode", SapFieldKind.Text, 254, description: "Primary bar code."),
        new("InventoryItem", SapFieldKind.YesNo, enumerationValues: null, description: "Stock-managed item."),
        new("SalesItem", SapFieldKind.YesNo, description: "May be sold."),
        new("PurchaseItem", SapFieldKind.YesNo, description: "May be purchased."),
        new("Valid", SapFieldKind.YesNo, description: "Active flag."),
        new("Frozen", SapFieldKind.YesNo, description: "Frozen (blocked) flag."),
        new("InventoryUOM", SapFieldKind.Text, 100, description: "Inventory unit of measure (non-UoM-group companies)."),
        new("SalesUnit", SapFieldKind.Text, 100, description: "Sales unit of measure."),
        new("PurchaseUnit", SapFieldKind.Text, 100, description: "Purchase unit of measure."),
        new("SalesItemsPerUnit", SapFieldKind.Decimal, description: "Items per sales unit."),
        new("PurchaseItemsPerUnit", SapFieldKind.Decimal, description: "Items per purchase unit."),
        new("UoMGroupEntry", SapFieldKind.Integer, description: "UoM group key (OUGP.AbsEntry), for UoM-group companies."),
        new("DefaultWarehouse", SapFieldKind.Text, 8, description: "Default warehouse code (OWHS.WhsCode)."),
        new("ManageStockByWarehouse", SapFieldKind.YesNo, description: "Manage stock per warehouse."),
        new("MinInventory", SapFieldKind.Decimal, description: "Minimum stock level."),
        new("MaxInventory", SapFieldKind.Decimal, description: "Maximum stock level."),
        new("Mainsupplier", SapFieldKind.Text, 15, description: "Preferred vendor code (OCRD.CardCode). Note the SAP spelling."),
        new("SupplierCatalogNo", SapFieldKind.Text, 50, description: "Vendor catalogue number."),
        new("Manufacturer", SapFieldKind.Integer, description: "Manufacturer key (OMRC.FirmCode)."),
        new("VatLiable", SapFieldKind.YesNo, description: "Subject to tax."),
        new("SalesVATGroup", SapFieldKind.Text, 8, description: "Sales tax group code."),
        new("PurchaseVATGroup", SapFieldKind.Text, 8, description: "Purchase tax group code."),
        new("AvgStdPrice", SapFieldKind.Decimal, description: "Average / standard cost."),
        new("GLMethod", SapFieldKind.Enumeration, enumerationValues: BuildGlMethodValues(), description: "G/L account determination method."),
        new("ManageSerialNumbers", SapFieldKind.YesNo, description: "Manage by serial numbers."),
        new("ManageBatchNumbers", SapFieldKind.YesNo, description: "Manage by batches."),
        new("User_Text", SapFieldKind.Text, description: "Item remarks / long description."),
    };

    private static readonly Dictionary<string, ItemFieldDefinition> ByNormalizedName = BuildLookup();

    /// <summary>All known field definitions, in catalog order.</summary>
    public static IReadOnlyList<ItemFieldDefinition> All => Definitions;

    /// <summary>
    /// Resolves a source column name to a field definition. Recognises the SAP property name, any
    /// documented alias, and any <c>U_*</c> user-defined field.
    /// </summary>
    public static bool TryResolve(
        string? columnName,
        [NotNullWhen(true)] out ItemFieldDefinition? definition)
    {
        definition = null;

        if (string.IsNullOrWhiteSpace(columnName))
        {
            return false;
        }

        var trimmed = columnName.Trim();

        if (trimmed.StartsWith(UserFieldPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // UDFs are company-specific; accept them verbatim and let SAP validate.
            definition = ItemFieldDefinition.PassThroughText(trimmed, "User-defined field.");
            return true;
        }

        if (ByNormalizedName.TryGetValue(SapNameNormalizer.Normalize(trimmed), out var found))
        {
            definition = found;
            return true;
        }

        return false;
    }

    /// <summary>Accepted input tokens for a <see cref="SapFieldKind.YesNo"/> field.</summary>
    public static IReadOnlyDictionary<string, string> BooleanTokens => YesNoValues;

    private static Dictionary<string, ItemFieldDefinition> BuildLookup()
    {
        var lookup = new Dictionary<string, ItemFieldDefinition>(StringComparer.Ordinal);

        foreach (var definition in Definitions)
        {
            lookup[SapNameNormalizer.Normalize(definition.SapProperty)] = definition;
        }

        // Friendly aliases that do not normalise to the SAP property name on their own.
        var aliases = new (string Alias, string SapProperty)[]
        {
            ("Code", "ItemCode"),
            ("SKU", "ItemCode"),
            ("Item No", "ItemCode"),
            ("Item Number", "ItemCode"),
            ("Name", "ItemName"),
            ("Description", "ItemName"),
            ("Item Description", "ItemName"),
            ("Item Group", "ItemsGroupCode"),
            ("Item Group Code", "ItemsGroupCode"),
            ("Group", "ItemsGroupCode"),
            ("Group Code", "ItemsGroupCode"),
            ("EAN", "BarCode"),
            ("UPC", "BarCode"),
            ("Active", "Valid"),
            ("Blocked", "Frozen"),
            ("UoM", "InventoryUOM"),
            ("Unit", "InventoryUOM"),
            ("Unit Of Measure", "InventoryUOM"),
            ("Inventory Unit", "InventoryUOM"),
            ("UoM Group", "UoMGroupEntry"),
            ("Warehouse", "DefaultWarehouse"),
            ("Whs Code", "DefaultWarehouse"),
            ("Default Whs", "DefaultWarehouse"),
            ("Min Stock", "MinInventory"),
            ("Minimum Stock", "MinInventory"),
            ("Max Stock", "MaxInventory"),
            ("Maximum Stock", "MaxInventory"),
            ("Main Supplier", "Mainsupplier"),
            ("Preferred Vendor", "Mainsupplier"),
            ("Vendor", "Mainsupplier"),
            ("Supplier", "Mainsupplier"),
            ("Vendor Catalog No", "SupplierCatalogNo"),
            ("Manufacturer Code", "Manufacturer"),
            ("Sales Tax Code", "SalesVATGroup"),
            ("Purchase Tax Code", "PurchaseVATGroup"),
            ("Average Price", "AvgStdPrice"),
            ("Standard Cost", "AvgStdPrice"),
            ("Cost", "AvgStdPrice"),
            ("Serial", "ManageSerialNumbers"),
            ("Batch", "ManageBatchNumbers"),
            ("Remarks", "User_Text"),
            ("Long Description", "User_Text"),
            ("Notes", "User_Text"),
        };

        var bySapProperty = new Dictionary<string, ItemFieldDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in Definitions)
        {
            bySapProperty[definition.SapProperty] = definition;
        }

        foreach (var (alias, sapProperty) in aliases)
        {
            if (!bySapProperty.TryGetValue(sapProperty, out var definition))
            {
                throw new InvalidOperationException($"Alias '{alias}' points at unknown SAP property '{sapProperty}'.");
            }

            lookup[SapNameNormalizer.Normalize(alias)] = definition;
        }

        return lookup;
    }

    private static IReadOnlyDictionary<string, string> BuildYesNoValues()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var token in new[] { "yes", "y", "true", "t", "1", "tyes", "x" })
        {
            values[token] = "tYES";
        }

        foreach (var token in new[] { "no", "n", "false", "f", "0", "tno" })
        {
            values[token] = "tNO";
        }

        return values;
    }

    private static IReadOnlyDictionary<string, string> BuildItemTypeValues()
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["item"] = "itItems",
            ["items"] = "itItems",
            ["ititems"] = "itItems",
            ["inventory"] = "itItems",
            ["labor"] = "itLabor",
            ["labour"] = "itLabor",
            ["itlabor"] = "itLabor",
            ["service"] = "itLabor",
            ["travel"] = "itTravel",
            ["ittravel"] = "itTravel",
            ["fixedasset"] = "itFixedAssets",
            ["fixedassets"] = "itFixedAssets",
            ["itfixedassets"] = "itFixedAssets",
        };

    private static IReadOnlyDictionary<string, string> BuildGlMethodValues()
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["warehouse"] = "glm_WH",
            ["wh"] = "glm_WH",
            ["glmwh"] = "glm_WH",
            ["itemgroup"] = "glm_ItemClass",
            ["itemclass"] = "glm_ItemClass",
            ["glmitemclass"] = "glm_ItemClass",
            ["item"] = "glm_ItemLevel",
            ["itemlevel"] = "glm_ItemLevel",
            ["glmitemlevel"] = "glm_ItemLevel",
        };
}
