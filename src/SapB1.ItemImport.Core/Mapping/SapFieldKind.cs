namespace SapB1.ItemImport.Core.Mapping;

/// <summary>How a source text value is converted before it is sent to SAP Business One.</summary>
public enum SapFieldKind
{
    /// <summary>Plain text, optionally length-checked.</summary>
    Text = 0,

    /// <summary>32-bit integer, typically a foreign key such as ItemsGroupCode.</summary>
    Integer = 1,

    /// <summary>Floating point number (SAP numeric/quantity/price fields).</summary>
    Decimal = 2,

    /// <summary>SAP <c>BoYesNoEnum</c>, serialised as <c>tYES</c> / <c>tNO</c>.</summary>
    YesNo = 3,

    /// <summary>Named SAP enumeration with a fixed set of accepted values.</summary>
    Enumeration = 4,

    /// <summary>Date, serialised as an ISO-8601 <c>yyyy-MM-dd</c> string.</summary>
    Date = 5,
}
