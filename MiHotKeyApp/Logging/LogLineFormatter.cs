namespace MiHotKeyApp.Logging;

using Microsoft.Extensions.Logging;

internal static class LogLineFormatter
{
    public static string FormatLine(DateTimeOffset now, LogLevel level, string category, string message)
    {
        var lvl = level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "NON",
        };

        return $"{now:HH:mm:ss.fff} [{lvl}] {category} - {message}";
    }
}

