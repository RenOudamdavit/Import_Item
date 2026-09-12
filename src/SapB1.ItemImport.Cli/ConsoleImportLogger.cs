using SapB1.ItemImport.Core.Logging;

namespace SapB1.ItemImport.Cli;

/// <summary>
/// Writes log entries to the console. Informational output goes to stdout and warnings/errors to
/// stderr, so a scheduled run can redirect the two separately.
/// </summary>
public sealed class ConsoleImportLogger : IImportLogger
{
    private readonly LogLevel _minimumLevel;
    private readonly object _gate = new();

    public ConsoleImportLogger(LogLevel minimumLevel = LogLevel.Information)
    {
        _minimumLevel = minimumLevel;
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (level < _minimumLevel)
        {
            return;
        }

        var writer = level >= LogLevel.Warning ? Console.Error : Console.Out;
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        var label = level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO ",
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            _ => "INFO ",
        };

        lock (_gate)
        {
            writer.WriteLine($"{stamp} {label} {message}");

            if (exception is not null)
            {
                writer.WriteLine($"{stamp} {label}   {exception.GetType().Name}: {exception.Message}");

                if (_minimumLevel == LogLevel.Debug && exception.StackTrace is not null)
                {
                    writer.WriteLine(exception.StackTrace);
                }
            }
        }
    }
}
