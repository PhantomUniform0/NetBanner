using Avalonia;
using NetBanner.Core;
using NetBanner.Windows;

namespace NetBanner.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0].StartsWith("--", StringComparison.Ordinal) && RunAdminCommand(args[0]) is { } exitCode)
        {
            return exitCode;
        }

        // One banner process per logon session (Local\ is per session, so each RDP session gets its own).
        using var mutex = new Mutex(true, @"Local\NetBanner.Banner", out bool isFirst);
        if (!isFirst)
        {
            return 0;
        }

        App.Options = AppOptions.Parse(args);
        Log.Sink = EventLogSink.Write;
        if (App.Options.LogFile is { } logFile)
        {
            var lockObj = new object();
            Log.Sink += (level, message) =>
            {
                lock (lockObj)
                {
                    File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
                }
            };
        }

        Log.Info($"NetBanner {typeof(Program).Assembly.GetName().Version} starting.");

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            Log.Error("NetBanner crashed.", ex);
            return 1;
        }
    }

    /// <summary>
    /// Installer helpers (run elevated by the MSI). Returns null if <paramref name="command"/> isn't one of them.
    /// Missing tasks on delete count as success, so the commands can always be run safely.
    /// </summary>
    private static int? RunAdminCommand(string command)
    {
        Log.Sink = EventLogSink.Write;
        (int Code, string Output)? result = command.ToLowerInvariant() switch
        {
            "--register-task" => TaskRegistration.Register(Environment.ProcessPath!),
            "--unregister-task" => Ok(TaskRegistration.Unregister()),
            "--remove-legacy-task" => TaskRegistration.UnregisterLegacy(Environment.ProcessPath!),
            "--start-task" => TaskRegistration.Start(),
            "--migrate-legacy-policy" => LegacyPolicyMigrator.Run(),
            "--register-sync-task" => TaskRegistration.RegisterSync(Environment.ProcessPath!),
            "--unregister-sync-task" => Ok(TaskRegistration.UnregisterSync()),
            "--start-sync-task" => TaskRegistration.StartSync(),
            "--server-sync" => ServerSyncLoop(),
            "--forget-server-key" => ForgetServerKey(),
            _ => null,
        };

        if (result is not { } r)
        {
            return null;
        }

        var message = $"NetBanner {command}: exit {r.Code}. {r.Output}";
        if (r.Code == 0)
        {
            Log.Info(message);
        }
        else
        {
            Log.Warn(message);
        }

        return r.Code;

        // schtasks /Delete fails when the task doesn't exist; that's the desired end state anyway.
        static (int, string) Ok((int Code, string Output) r) =>
            (0, r.Code == 0 ? r.Output : $"Nothing to remove (task not present). schtasks said: {r.Output}");
    }

    /// <summary>Runs as SYSTEM (scheduled task) until the task is ended: keeps the server config in sync.</summary>
    private static (int, string) ServerSyncLoop()
    {
        using var cts = new CancellationTokenSource();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();
        using var sync = new ServerSync();
        sync.RunAsync(() => ConfigFile.Load(ConfigStore.DefaultPath, new List<string>())?.ServerUrl,
                ConfigStore.StateDir, TimeSpan.FromSeconds(60), cts.Token)
            .GetAwaiter().GetResult();
        return (0, "Server sync stopped.");
    }

    private static (int, string) ForgetServerKey()
    {
        ServerSync.ForgetServerKey(ConfigStore.StateDir);
        TaskRegistration.StartSync();
        return (0, "Server key forgotten; the sync task was restarted and will pin the server's current key.");
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
