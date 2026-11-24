namespace NetBanner.Core;

public enum LogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>Minimal logging facade. The platform layer plugs in a sink (Event Log, journald, ...).</summary>
public static class Log
{
    public static Action<LogLevel, string>? Sink { get; set; }

    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warning, message);
    public static void Error(string message, Exception? ex = null) =>
        Write(LogLevel.Error, ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private static void Write(LogLevel level, string message)
    {
        System.Diagnostics.Trace.WriteLine($"[NetBanner {level}] {message}");
        try
        {
            Sink?.Invoke(level, message);
        }
        catch
        {
            // Logging must never take the banner down.
        }
    }
}
