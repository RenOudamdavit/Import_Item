using System.Globalization;
using System.Text.RegularExpressions;
using SapB1.ItemImport.Core.Csv;
using SapB1.ItemImport.Core.Domain;
using SapB1.ItemImport.Core.Validation;

namespace SapB1.ItemImport.Core.Mapping;

/// <summary>
/// Binds the columns of a source file to SAP item properties once, then converts each row.
/// </summary>
/// <remarks>
/// Binding happens in the constructor so that structural problems — a missing ItemCode column, a typo
/// in a header, two columns feeding the same SAP property — are reported before a single row is sent
/// to SAP, rather than surfacing as thousands of identical per-row failures.
/// </remarks>
public sealed class ItemRecordBuilder
{
    private static readonly Regex PriceColumnPattern = new(
        @"^(?<part>pricelistcode|pricelist|unitprice|price|currency)(?<index>[0-9]{0,3})$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    private readonly ItemRecordBuilderOptions _options;
    private readonly List<ScalarBinding> _scalarBindings = new();
    private readonly List<PriceBinding> _priceBindings = new();
    private readonly List<ValidationMessage> _headerMessages = new();
    private readonly Regex? _itemCodePattern;
    private readonly string _itemCodeColumn;

    public ItemRecordBuilder(CsvHeader header, ItemRecordBuilderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(header);

        _options = options ?? new ItemRecordBuilderOptions();

        if (!string.IsNullOrWhiteSpace(_options.ItemCodePattern))
        {
            try
            {
                _itemCodePattern = new Regex(_options.ItemCodePattern, RegexOptions.CultureInvariant);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException(
                    $"ItemCodePattern '{_options.ItemCodePattern}' is not a valid regular expression: {ex.Message}",
                    nameof(options),
                    ex);
            }
        }

        var priceColumns = new Dictionary<int, PriceColumnGroup>();
        var boundProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? itemCodeColumn = null;

        foreach (var column in header.Columns)
        {
            if (_options.ColumnOverrides.TryGetValue(column, out var overrideProperty))
            {
                if (string.IsNullOrWhiteSpace(overrideProperty))
                {
                    // An empty override is the documented way to drop a column deliberately.
                    continue;
                }

                var target = overrideProperty.Trim();
                var overrideMatch = PriceColumnPattern.Match(SapNameNormalizer.Normalize(target));

                if (overrideMatch.Success)
                {
                    // An override may point at a price column (e.g. "Verkaufspreis" -> "Price"),
                    // which belongs in the ItemPrices collection rather than on the item itself.
                    BindPriceColumn(column, overrideMatch, priceColumns);
                    continue;
                }

                var definition = ItemFieldCatalog.TryResolve(target, out var known)
                    ? known!
                    : ItemFieldDefinition.PassThroughText(target, "Mapped by configuration.");

                AddScalarBinding(column, definition, boundProperties, ref itemCodeColumn);
                continue;
            }

            var normalized = SapNameNormalizer.Normalize(column);
            var priceMatch = PriceColumnPattern.Match(normalized);
            if (priceMatch.Success)
            {
                BindPriceColumn(column, priceMatch, priceColumns);
                continue;
            }

            if (ItemFieldCatalog.TryResolve(column, out var catalogDefinition))
            {
                AddScalarBinding(column, catalogDefinition!, boundProperties, ref itemCodeColumn);
                continue;
            }

            switch (_options.UnknownColumns)
            {
                case UnknownColumnBehavior.Error:
                    _headerMessages.Add(ValidationMessage.Error(
                        column,
                        "column is not a known SAP item field. Rename it, map it in the mapping file, or remove it."));
                    break;
                case UnknownColumnBehavior.Warn:
                    _headerMessages.Add(ValidationMessage.Warning(
                        column,
                        "column is not a known SAP item field and will be ignored."));
                    break;
                case UnknownColumnBehavior.Ignore:
                default:
                    break;
            }
        }

        if (itemCodeColumn is null)
        {
            _headerMessages.Add(ValidationMessage.Error(
                ItemFieldCatalog.ItemCodeProperty,
                "the file has no ItemCode column. Add a column named ItemCode (or Item Code / Code / SKU)."));
        }

        _itemCodeColumn = itemCodeColumn ?? ItemFieldCatalog.ItemCodeProperty;

        foreach (var (index, group) in priceColumns.OrderBy(pair => pair.Key))
        {
            _priceBindings.Add(new PriceBinding(index, group.PriceListColumn, group.PriceColumn, group.CurrencyColumn));
        }
    }

    /// <summary>Structural findings about the header row. Errors here mean the file must not be imported.</summary>
    public IReadOnlyList<ValidationMessage> HeaderMessages => _headerMessages;

    /// <summary><c>true</c> when the header is usable.</summary>
    public bool IsHeaderValid => !_headerMessages.Any(m => m.Severity == ValidationSeverity.Error);

    /// <summary>SAP properties this file will write, excluding the primary key.</summary>
    public IReadOnlyList<string> MappedProperties
        => _scalarBindings
            .Where(b => !IsItemCode(b.Field.SapProperty))
            .Select(b => b.Field.SapProperty)
            .ToList();

    /// <summary>Converts and validates a single source row.</summary>
    public ItemRecordParseResult Build(CsvRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var messages = new List<ValidationMessage>();
        var itemCode = (row[_itemCodeColumn] ?? string.Empty).Trim();

        ValidateItemCode(itemCode, messages);

        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var binding in _scalarBindings)
        {
            if (IsItemCode(binding.Field.SapProperty))
            {
                continue;
            }

            var raw = row[binding.Column];
            if (string.IsNullOrWhiteSpace(raw))
            {
                // Absent cell means "leave whatever SAP already has".
                continue;
            }

            if (SapValueConverter.TryConvert(binding.Field, raw, _options.Conversion, out var value, out var error))
            {
                fields[binding.Field.SapProperty] = value;
            }
            else
            {
                messages.Add(ValidationMessage.Error(binding.Column, error ?? "value could not be converted."));
            }
        }

        var prices = BuildPrices(row, messages);

        if (messages.Any(m => m.Severity == ValidationSeverity.Error))
        {
            return ItemRecordParseResult.Failure(row.LineNumber, itemCode, messages);
        }

        if (fields.Count == 0 && prices.Count == 0)
        {
            messages.Add(ValidationMessage.Warning(
                string.Empty,
                "row supplies no values besides the item code; nothing would be written."));
        }

        var record = new ItemRecord
        {
            SourceLineNumber = row.LineNumber,
            ItemCode = itemCode,
            Fields = fields,
            Prices = prices,
        };

        return ItemRecordParseResult.Success(record, messages);
    }

    private void ValidateItemCode(string itemCode, List<ValidationMessage> messages)
    {
        if (string.IsNullOrEmpty(itemCode))
        {
            messages.Add(ValidationMessage.Error(_itemCodeColumn, "item code is required."));
            return;
        }

        const int maxItemCodeLength = 50;
        if (itemCode.Length > maxItemCodeLength)
        {
            messages.Add(ValidationMessage.Error(
                _itemCodeColumn,
                $"item code is {itemCode.Length} characters but SAP allows at most {maxItemCodeLength}."));
        }

        foreach (var current in itemCode)
        {
            if (char.IsControl(current))
            {
                messages.Add(ValidationMessage.Error(
                    _itemCodeColumn,
                    "item code contains a control character. Check the source file for stray tabs or line breaks."));
                break;
            }
        }

        if (_itemCodePattern is not null && !_itemCodePattern.IsMatch(itemCode))
        {
            messages.Add(ValidationMessage.Error(
                _itemCodeColumn,
                $"item code '{itemCode}' does not match the required pattern {_options.ItemCodePattern}."));
        }
    }

    private List<ItemPrice> BuildPrices(CsvRow row, List<ValidationMessage> messages)
    {
        var prices = new List<ItemPrice>();
        var seenPriceLists = new HashSet<int>();

        foreach (var binding in _priceBindings)
        {
            var rawPriceList = binding.PriceListColumn is null ? null : row[binding.PriceListColumn];
            var rawPrice = binding.PriceColumn is null ? null : row[binding.PriceColumn];
            var rawCurrency = binding.CurrencyColumn is null ? null : row[binding.CurrencyColumn];

            if (string.IsNullOrWhiteSpace(rawPriceList)
                && string.IsNullOrWhiteSpace(rawPrice)
                && string.IsNullOrWhiteSpace(rawCurrency))
            {
                continue;
            }

            var label = binding.PriceColumn ?? binding.PriceListColumn ?? binding.CurrencyColumn ?? "Price";

            int priceList;
            if (!string.IsNullOrWhiteSpace(rawPriceList))
            {
                if (!int.TryParse(rawPriceList, NumberStyles.Integer, _options.Conversion.Culture, out priceList))
                {
                    messages.Add(ValidationMessage.Error(
                        binding.PriceListColumn ?? label,
                        $"'{rawPriceList}' is not a valid price list number."));
                    continue;
                }

                if (priceList <= 0)
                {
                    messages.Add(ValidationMessage.Error(
                        binding.PriceListColumn ?? label,
                        "price list number must be greater than zero."));
                    continue;
                }
            }
            else if (binding.Index == 1)
            {
                priceList = _options.DefaultPriceList;
            }
            else
            {
                messages.Add(ValidationMessage.Error(
                    label,
                    $"PriceList{binding.Index} is required because Price{binding.Index} was supplied."));
                continue;
            }

            if (!seenPriceLists.Add(priceList))
            {
                messages.Add(ValidationMessage.Error(
                    label,
                    $"price list {priceList} is supplied more than once in this row."));
                continue;
            }

            double? price = null;
            if (!string.IsNullOrWhiteSpace(rawPrice))
            {
                if (!double.TryParse(rawPrice, NumberStyles.Float | NumberStyles.AllowThousands, _options.Conversion.Culture, out var parsedPrice))
                {
                    messages.Add(ValidationMessage.Error(
                        binding.PriceColumn ?? label,
                        $"'{rawPrice}' is not a valid price."));
                    continue;
                }

                if (double.IsNaN(parsedPrice) || double.IsInfinity(parsedPrice))
                {
                    messages.Add(ValidationMessage.Error(binding.PriceColumn ?? label, $"'{rawPrice}' is not a finite price."));
                    continue;
                }

                if (parsedPrice < 0)
                {
                    messages.Add(ValidationMessage.Error(binding.PriceColumn ?? label, "price must not be negative."));
                    continue;
                }

                price = parsedPrice;
            }

            string? currency = null;
            if (!string.IsNullOrWhiteSpace(rawCurrency))
            {
                currency = rawCurrency.Trim();
                if (currency.Length > 3)
                {
                    messages.Add(ValidationMessage.Error(
                        binding.CurrencyColumn ?? label,
                        $"currency code '{currency}' is longer than 3 characters."));
                    continue;
                }
            }

            prices.Add(new ItemPrice
            {
                PriceList = priceList,
                Price = price,
                Currency = currency,
            });
        }

        return prices;
    }

    private void AddScalarBinding(
        string column,
        ItemFieldDefinition definition,
        Dictionary<string, string> boundProperties,
        ref string? itemCodeColumn)
    {
        if (boundProperties.TryGetValue(definition.SapProperty, out var existingColumn))
        {
            _headerMessages.Add(ValidationMessage.Error(
                column,
                $"both '{existingColumn}' and '{column}' map to SAP property {definition.SapProperty}. Remove or remap one of them."));
            return;
        }

        boundProperties[definition.SapProperty] = column;

        if (IsItemCode(definition.SapProperty))
        {
            itemCodeColumn = column;
        }

        _scalarBindings.Add(new ScalarBinding(column, definition));
    }

    private void BindPriceColumn(string column, Match match, Dictionary<int, PriceColumnGroup> priceColumns)
    {
        var part = match.Groups["part"].Value;
        var rawIndex = match.Groups["index"].Value;
        var index = rawIndex.Length == 0
            ? 1
            : int.Parse(rawIndex, NumberStyles.Integer, CultureInfo.InvariantCulture);

        if (!priceColumns.TryGetValue(index, out var group))
        {
            group = new PriceColumnGroup();
            priceColumns[index] = group;
        }

        switch (part)
        {
            case "pricelist":
            case "pricelistcode":
                if (group.PriceListColumn is not null)
                {
                    _headerMessages.Add(ValidationMessage.Error(column, $"duplicate price list column for price entry {index}."));
                    return;
                }

                group.PriceListColumn = column;
                return;

            case "price":
            case "unitprice":
                if (group.PriceColumn is not null)
                {
                    _headerMessages.Add(ValidationMessage.Error(column, $"duplicate price column for price entry {index}."));
                    return;
                }

                group.PriceColumn = column;
                return;

            case "currency":
                if (group.CurrencyColumn is not null)
                {
                    _headerMessages.Add(ValidationMessage.Error(column, $"duplicate currency column for price entry {index}."));
                    return;
                }

                group.CurrencyColumn = column;
                return;

            default:
                return;
        }
    }

    private static bool IsItemCode(string sapProperty)
        => string.Equals(sapProperty, ItemFieldCatalog.ItemCodeProperty, StringComparison.OrdinalIgnoreCase);

    private readonly record struct ScalarBinding(string Column, ItemFieldDefinition Field);

    private readonly record struct PriceBinding(int Index, string? PriceListColumn, string? PriceColumn, string? CurrencyColumn);

    private sealed class PriceColumnGroup
    {
        public string? PriceListColumn { get; set; }

        public string? PriceColumn { get; set; }

        public string? CurrencyColumn { get; set; }
    }
}
