namespace SapB1.ItemImport.Core.Mapping;

/// <summary>Describes one writable property of the SAP Business One <c>Items</c> entity.</summary>
public sealed class ItemFieldDefinition
{
    public ItemFieldDefinition(
        string sapProperty,
        SapFieldKind kind,
        int? maxLength = null,
        IReadOnlyDictionary<string, string>? enumerationValues = null,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sapProperty);

        if (kind == SapFieldKind.Enumeration && (enumerationValues is null || enumerationValues.Count == 0))
        {
            throw new ArgumentException(
                $"Field '{sapProperty}' is an enumeration but no accepted values were supplied.",
                nameof(enumerationValues));
        }

        SapProperty = sapProperty;
        Kind = kind;
        MaxLength = maxLength;
        EnumerationValues = enumerationValues;
        Description = description;
    }

    /// <summary>Property name exactly as the SAP Business One Service Layer expects it.</summary>
    public string SapProperty { get; }

    public SapFieldKind Kind { get; }

    /// <summary>Maximum length enforced by SAP, checked before the call so the failure is readable.</summary>
    public int? MaxLength { get; }

    /// <summary>Accepted input token (normalised) to SAP enumeration value.</summary>
    public IReadOnlyDictionary<string, string>? EnumerationValues { get; }

    public string? Description { get; }

    /// <summary>Creates a pass-through text definition for a user-defined field or an unlisted property.</summary>
    public static ItemFieldDefinition PassThroughText(string sapProperty, string? description = null)
        => new(sapProperty, SapFieldKind.Text, maxLength: null, enumerationValues: null, description);
}
