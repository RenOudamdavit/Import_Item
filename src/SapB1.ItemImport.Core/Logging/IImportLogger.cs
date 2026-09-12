namespace SapB1.ItemImport.Core.Logging;

/// <summary>Severity of a log entry emitted by the import pipeline.</summary>
public enum LogLevel
{
    Debug = 0,
    Information = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>
/// Minimal logging abstraction so the core pipeline stays free of any logging framework.
/// Implementations must never write credentials; the pipeline already redacts them before calling.
/// </summary>
public interface IImportLogger
{
    void Log(LogLevel level, string message, Exception? exception = null);
}

/// <summary>Convenience helpers over <see cref="IImportLogger"/>.</summary>
public static class ImportLoggerExtensions
{
    public static void Debug(this IImportLogger logger, string message)
        => logger.Log(LogLevel.Debug, message);

    public static void Info(this IImportLogger logger, string message)
        => logger.Log(LogLevel.Information, message);

    public static void Warn(this IImportLogger logger, string message)
        => logger.Log(LogLevel.Warning, message);

    public static void Error(this IImportLogger logger, string message, Exception? exception = null)
        => logger.Log(LogLevel.Error, message, exception);
}

/// <summary>Logger that discards everything. Useful in tests.</summary>
public sealed class NullImportLogger : IImportLogger
{
    public static readonly NullImportLogger Instance = new();

    private NullImportLogger()
    {
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        // Intentionally empty.
    }
}
