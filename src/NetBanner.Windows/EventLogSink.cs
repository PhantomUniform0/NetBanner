using System.Diagnostics;
using NetBanner.Core;

namespace NetBanner.Windows;

/// <summary>
/// Writes to the Application event log under source "NetBanner". The installer registers the source.
/// Without it (e.g. dev runs), writes fail quietly and logging falls back to Trace only.
/// </summary>
public static class EventLogSink
{
    public const string SourceName = "NetBanner";
    private static bool _disabled;

    public static void Write(LogLevel level, string message)
    {
        if (_disabled)
        {
            return;
        }

        var type = level switch
        {
            LogLevel.Error => EventLogEntryType.Error,
            LogLevel.Warning => EventLogEntryType.Warning,
            _ => EventLogEntryType.Information,
        };

        try
        {
            // Event log entries are limited to ~32K characters.
            if (message.Length > 30000)
            {
                message = message[..30000];
            }

            EventLog.WriteEntry(SourceName, message, type, (int)level + 1000);
        }
        catch
        {
            // Source not registered and no rights to create it. Stop trying.
            _disabled = true;
        }
    }
}
