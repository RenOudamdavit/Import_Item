namespace SapB1.ItemImport.Core.Mapping;

/// <summary>What to do with a source column the importer does not recognise.</summary>
public enum UnknownColumnBehavior
{
    /// <summary>Drop the column silently.</summary>
    Ignore = 0,

    /// <summary>Drop the column but warn once, before the import starts. Default.</summary>
    Warn = 1,

    /// <summary>Refuse to run. Use this for controlled migrations where a typo must not be ignored.</summary>
    Error = 2,
}
