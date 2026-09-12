using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using SAPbobsCOM;
using SapB1.ItemImport.Core.Abstractions;
using SapB1.ItemImport.Core.Domain;
using SapB1.ItemImport.Core.Logging;
using SapB1.ItemImport.Core.Mapping;

namespace SapB1.ItemImport.DiApi;

/// <summary>
/// <see cref="IItemGateway"/> over the SAP Business One DI API (COM).
/// </summary>
/// <remarks>
/// Use this where the Service Layer is not an option — an older installation, or a policy that only
/// permits the SAP client stack. It requires Windows, a matching DI API installation and a matching
/// process bitness. The DI API is synchronous and apartment-bound, so the methods here complete on the
/// calling thread and return already-finished tasks rather than moving COM calls onto the thread pool.
/// The same <see cref="ItemRecord"/> values that the Service Layer gateway consumes work unchanged:
/// SAP enumeration member names such as <c>tYES</c> and <c>itItems</c> are resolved onto the COM enums.
/// </remarks>
public sealed class DiApiItemGateway : IItemGateway
{
    private readonly DiApiOptions _options;
    private readonly IImportLogger _logger;
    private Company? _company;

    public DiApiItemGateway(DiApiOptions options, IImportLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();
        _options = options;
        _logger = logger ?? NullImportLogger.Instance;
    }

    public string Description => $"SAP Business One DI API ({_options.Server}, company {_options.CompanyDb})";

    private Company Company =>
        _company ?? throw new SapConnectionException("ConnectAsync must be called before using the DI API gateway.");

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_company is not null)
        {
            return Task.CompletedTask;
        }

        if (!Enum.TryParse<BoDataServerTypes>(_options.DbServerType, ignoreCase: true, out var serverType))
        {
            throw new SapConnectionException(
                $"'{_options.DbServerType}' is not a valid BoDataServerTypes value. Use for example dst_MSSQL2019 or dst_HANADB.");
        }

        var company = new Company
        {
            Server = _options.Server,
            CompanyDB = _options.CompanyDb,
            LicenseServer = _options.LicenseServer,
            DbServerType = serverType,
            UseTrusted = _options.UseTrusted,
            DbUserName = _options.DbUserName,
            DbPassword = _options.DbPassword,
            UserName = _options.UserName,
            Password = _options.Password,
        };

        if (!string.IsNullOrWhiteSpace(_options.Language)
            && Enum.TryParse<BoSuppLangs>(_options.Language, ignoreCase: true, out var language))
        {
            company.language = language;
        }

        int result;
        try
        {
            result = company.Connect();
        }
        catch (COMException ex)
        {
            Release(company);
            throw new SapConnectionException(
                $"The DI API could not connect to '{_options.CompanyDb}'. Check that the DI API version and process "
                + $"bitness match the SAP installation. {ex.Message}",
                ex);
        }

        if (result != 0)
        {
            company.GetLastError(out var code, out var message);
            Release(company);

            throw new SapConnectionException(
                $"The DI API could not connect to company '{_options.CompanyDb}' on '{_options.Server}' "
                + $"[SAP {code.ToString(CultureInfo.InvariantCulture)}]: {message}");
        }

        _company = company;
        _logger.Info($"Connected through the DI API to company {_options.CompanyDb} on {_options.Server}.");

        return Task.CompletedTask;
    }

    public Task<HashSet<string>> GetExistingItemCodesAsync(
        IReadOnlyCollection<string> itemCodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemCodes);

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (itemCodes.Count == 0)
        {
            return Task.FromResult(existing);
        }

        // GetByKey per code rather than a Recordset query: it costs an extra round trip per item but
        // needs no hand-built SQL, so there is no dialect difference between SQL Server and HANA and
        // no place for an item code to be interpolated into a statement.
        var items = (Items)Company.GetBusinessObject(BoObjectTypes.oItems);

        try
        {
            foreach (var itemCode in itemCodes.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(itemCode))
                {
                    continue;
                }

                if (items.GetByKey(itemCode))
                {
                    existing.Add(itemCode);
                }
            }
        }
        finally
        {
            Release(items);
        }

        return Task.FromResult(existing);
    }

    public Task CreateAsync(ItemRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        var items = (Items)Company.GetBusinessObject(BoObjectTypes.oItems);

        try
        {
            items.ItemCode = record.ItemCode;
            ApplyFields(items, record);
            ApplyPrices(items, record);

            if (items.Add() != 0)
            {
                throw LastErrorAsRejection("create");
            }

            _logger.Debug($"Created item {record.ItemCode} through the DI API.");
        }
        finally
        {
            Release(items);
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(ItemRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        if (record.Fields.Count == 0 && record.Prices.Count == 0)
        {
            _logger.Debug($"Item {record.ItemCode} has nothing to update; skipping the call.");
            return Task.CompletedTask;
        }

        var items = (Items)Company.GetBusinessObject(BoObjectTypes.oItems);

        try
        {
            if (!items.GetByKey(record.ItemCode))
            {
                throw new SapItemRejectedException(
                    $"Item '{record.ItemCode}' no longer exists in SAP, so it could not be updated.");
            }

            ApplyFields(items, record);
            ApplyPrices(items, record);

            if (items.Update() != 0)
            {
                throw LastErrorAsRejection("update");
            }

            _logger.Debug($"Updated item {record.ItemCode} through the DI API.");
        }
        finally
        {
            Release(items);
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        var company = _company;
        _company = null;

        if (company is null)
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            if (company.Connected)
            {
                company.Disconnect();
            }
        }
        catch (COMException ex)
        {
            _logger.Debug($"DI API disconnect failed and was ignored: {ex.Message}");
        }
        finally
        {
            Release(company);

            // The DI API is notorious for holding native memory until the RCWs are finalised.
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        return ValueTask.CompletedTask;
    }

    private static void ApplyFields(Items items, ItemRecord record)
    {
        foreach (var (property, value) in record.Fields)
        {
            if (property.StartsWith(ItemFieldCatalog.UserFieldPrefix, StringComparison.OrdinalIgnoreCase))
            {
                SetUserField(items, property, value);
                continue;
            }

            var info = typeof(Items).GetProperty(property, BindingFlags.Public | BindingFlags.Instance);

            if (info is null || !info.CanWrite)
            {
                throw new SapItemRejectedException(
                    $"'{property}' is not a writable property of the DI API item object. "
                    + "Check the spelling, or use the Service Layer gateway if the property only exists there.");
            }

            info.SetValue(items, ConvertForCom(info.PropertyType, value, property));
        }
    }

    private static void SetUserField(Items items, string property, object? value)
    {
        var userFields = items.UserFields;

        try
        {
            var fields = userFields.Fields;

            try
            {
                Field field;
                try
                {
                    field = fields.Item(property);
                }
                catch (COMException ex)
                {
                    throw new SapItemRejectedException(
                        $"The user-defined field '{property}' does not exist on the item master in this company.",
                        sapErrorCode: null,
                        ex);
                }

                try
                {
                    // The DI API rejects a null; an empty string is how an alphanumeric UDF is cleared.
                    field.Value = value ?? string.Empty;
                }
                finally
                {
                    Release(field);
                }
            }
            finally
            {
                Release(fields);
            }
        }
        finally
        {
            Release(userFields);
        }
    }

    private static void ApplyPrices(Items items, ItemRecord record)
    {
        if (record.Prices.Count == 0)
        {
            return;
        }

        var priceLines = items.PriceList;

        try
        {
            foreach (var price in record.Prices)
            {
                // DI API price lines are pre-created, one per price list, indexed from zero.
                if (price.PriceList < 1 || price.PriceList > priceLines.Count)
                {
                    throw new SapItemRejectedException(
                        $"Price list {price.PriceList.ToString(CultureInfo.InvariantCulture)} does not exist in this "
                        + $"company (it defines {priceLines.Count.ToString(CultureInfo.InvariantCulture)} price lists).");
                }

                priceLines.SetCurrentLine(price.PriceList - 1);

                if (price.Price.HasValue)
                {
                    priceLines.Price = price.Price.Value;
                }

                if (!string.IsNullOrEmpty(price.Currency))
                {
                    priceLines.Currency = price.Currency;
                }
            }
        }
        finally
        {
            Release(priceLines);
        }
    }

    /// <summary>
    /// Converts a value produced for the Service Layer into what the COM property expects. SAP
    /// enumeration member names (<c>tYES</c>, <c>itItems</c>, <c>glm_WH</c>) map straight onto the enums.
    /// </summary>
    private static object? ConvertForCom(Type targetType, object? value, string property)
    {
        var target = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (value is null)
        {
            if (target == typeof(string))
            {
                return string.Empty;
            }

            throw new SapItemRejectedException(
                $"'{property}' cannot be cleared through the DI API. Remove the null token from this cell, "
                + "or run the import against the Service Layer.");
        }

        try
        {
            if (target.IsEnum)
            {
                return value is string token
                    ? Enum.Parse(target, token, ignoreCase: true)
                    : Enum.ToObject(target, Convert.ToInt64(value, CultureInfo.InvariantCulture));
            }

            return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidCastException or OverflowException)
        {
            throw new SapItemRejectedException(
                $"Value '{value}' is not valid for '{property}' (expected {target.Name}): {ex.Message}",
                sapErrorCode: null,
                ex);
        }
    }

    private SapItemRejectedException LastErrorAsRejection(string operation)
    {
        Company.GetLastError(out var code, out var message);

        return new SapItemRejectedException(
            $"SAP refused to {operation} the item [SAP {code.ToString(CultureInfo.InvariantCulture)}]: {message}",
            code.ToString(CultureInfo.InvariantCulture));
    }

    private static void Release(object? comObject)
    {
        if (comObject is null || !Marshal.IsComObject(comObject))
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(comObject);
        }
        catch (ArgumentException)
        {
            // Already released; nothing to do.
        }
    }
}
