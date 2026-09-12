namespace SapB1.ItemImport.Core.Validation;

public enum ValidationSeverity
{
    Warning = 0,
    Error = 1,
}

/// <summary>A single operator-facing validation finding, anchored to a source column.</summary>
/// <param name="Severity">Whether the row can still be imported.</param>
/// <param name="Column">Source column the finding relates to; empty for row-level findings.</param>
/// <param name="Message">Human-readable explanation, safe to print or hand to an end user.</param>
public sealed record ValidationMessage(ValidationSeverity Severity, string Column, string Message)
{
    public static ValidationMessage Error(string column, string message)
        => new(ValidationSeverity.Error, column, message);

    public static ValidationMessage Warning(string column, string message)
        => new(ValidationSeverity.Warning, column, message);

    public override string ToString()
        => string.IsNullOrEmpty(Column) ? Message : $"{Column}: {Message}";
}
