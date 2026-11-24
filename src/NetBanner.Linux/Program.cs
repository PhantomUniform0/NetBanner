using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using NetBanner.Core;

namespace NetBanner.Linux;

/// <summary>
/// <c>netbanner</c> on Linux.
/// <list type="bullet">
/// <item><c>netbanner render</c>: the resolved banner as JSON (used by the GNOME Shell extension).</item>
/// <item><c>netbanner validate</c>: checks the config file; exit 0 = fine, 2 = warnings.</item>
/// <item><c>netbanner banner</c>: runs the banner as an X11 dock window (KDE Plasma; started by the systemd user unit).</item>
/// <item><c>netbanner should-run</c>: exit 0 when the X11 banner should run in this session (systemd ExecCondition).</item>
/// <item><c>netbanner server-sync</c>: keeps the NetBanner server's config in sync (root; netbanner-sync.service).</item>
/// <item><c>netbanner forget-server-key</c>: accept a new server key on the next sync (root).</item>
/// </list>
/// All commands take <c>--config &lt;path&gt;</c>; default <c>/etc/netbanner/config.json</c>.
/// </summary>
internal static class Program
{
    public static string ConfigPath { get; private set; } = LinuxSettings.DefaultPath;

    [STAThread]
    public static int Main(string[] args)
    {
        string? config = null;
        var rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--config" && i + 1 < args.Length)
            {
                config = args[++i];
            }
            else
            {
                rest.Add(args[i]);
            }
        }

        ConfigPath = LinuxSettings.ResolvePath(config);
        var command = rest.FirstOrDefault() ?? "help";
        // No Log.Sink on Linux: Log also writes through Trace, which already reaches stderr (the journal); a sink
        // would print every line twice. Commands that talk to the admin (validate, render) use Console directly.

        return command switch
        {
            "render" => Render(),
            "validate" => Validate(),
            "banner" => RunBanner(rest.Skip(1).ToArray()),
            "should-run" => ShouldRun(),
            "server-sync" => ServerSyncLoop(),
            "forget-server-key" => ForgetServerKey(),
            "--version" or "version" => Print(typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "?"),
            _ => Usage(),
        };
    }

    private static int Render()
    {
        var warnings = new List<string>();
        var settings = LinuxSettings.Load(ConfigPath, warnings);
        var view = BannerPresenter.Present(settings, Tokens.FromSystem);
        var output = new RenderOutput(
            view.CenterText, view.Background, view.Foreground,
            view.Left.Select(i => new RenderItem(i.Text, i.Background, i.Foreground)).ToList(),
            view.Right.Select(i => new RenderItem(i.Text, i.Background, i.Foreground)).ToList(),
            settings.Height, settings.FontSize, settings.FullScreenBehavior.ToString(), warnings);
        Console.Out.WriteLine(JsonSerializer.Serialize(output, RenderJsonContext.Default.RenderOutput));
        return 0;
    }

    private static int Validate()
    {
        var warnings = new List<string>();
        if (!File.Exists(ConfigPath))
        {
            Console.Out.WriteLine($"{ConfigPath} not found: the banner shows CLASSIFICATION NOT CONFIGURED.");
            return 2;
        }

        var settings = LinuxSettings.Load(ConfigPath, warnings);
        var view = BannerPresenter.Present(settings, Tokens.FromSystem);
        Console.Out.WriteLine($"Banner: {view.CenterText}  (background {view.Background}, text {view.Foreground})");
        if (view.Left.Count > 0)
        {
            Console.Out.WriteLine("Left:   " + string.Join(" | ", view.Left.Select(i => i.Text)));
        }

        if (view.Right.Count > 0)
        {
            Console.Out.WriteLine("Right:  " + string.Join(" | ", view.Right.Select(i => i.Text)));
        }

        if (LinuxSettings.ServerUrl(ConfigPath) is { } url)
        {
            Console.Out.WriteLine($"Server: {url}  (pinned key {ServerSync.PinnedFingerprint(LinuxSettings.StateDir) ?? "none yet"}, " +
                                  $"cached config {(File.Exists(LinuxSettings.ServerCachePath) ? "present" : "none yet")})");
        }

        foreach (var w in warnings)
        {
            Console.Out.WriteLine("Warning: " + w);
        }

        return warnings.Count == 0 ? 0 : 2;
    }

    private static int ServerSyncLoop()
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();
        using var sync = new ServerSync();
        sync.RunAsync(() => LinuxSettings.ServerUrl(ConfigPath), LinuxSettings.StateDir, TimeSpan.FromSeconds(60), cts.Token)
            .GetAwaiter().GetResult();
        return 0;
    }

    private static int ForgetServerKey()
    {
        ServerSync.ForgetServerKey(LinuxSettings.StateDir);
        Console.Out.WriteLine("Server key forgotten; the next sync pins the server's current key. " +
                              "Restart the sync to do it now: systemctl restart netbanner-sync");
        return 0;
    }

    /// <summary>
    /// GNOME draws the banner with its Shell extension, so the X11 banner only runs on other desktops (KDE Plasma).
    /// </summary>
    private static int ShouldRun()
    {
        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty;
        bool gnome = desktop.Split(':').Any(d => d.Equals("GNOME", StringComparison.OrdinalIgnoreCase));
        bool display = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));
        return !gnome && display ? 0 : 1;
    }

    private static int RunBanner(string[] args)
    {
        Log.Info($"NetBanner {typeof(Program).Assembly.GetName().Version?.ToString(3)} starting (config {ConfigPath}).");
        try
        {
            return AppBuilder.Configure<LinuxApp>()
                .UsePlatformDetect()
                .With(new X11PlatformOptions { EnableIme = false })
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            Log.Error("NetBanner crashed.", ex);
            return 1;
        }
    }

    private static int Print(string s)
    {
        Console.Out.WriteLine(s);
        return 0;
    }

    private static int Usage()
    {
        Console.Out.WriteLine("""
            Usage: netbanner <command> [--config <path>]

              validate     Check the config file and show the resulting banner (exit 0 = OK, 2 = warnings)
              render       Print the resolved banner as JSON (used by the GNOME Shell extension)
              banner       Run the banner (X11 dock; started automatically on KDE Plasma)
              server-sync  Keep the NetBanner server's config in sync (root; runs as netbanner-sync.service)
              forget-server-key  Accept a new server key on the next sync (root)
              version      Print the version

            Config file: /etc/netbanner/config.json (override with --config or NETBANNER_CONFIG)
            """);
        return 1;
    }
}

internal sealed record RenderItem(string Text, string? Background, string? Foreground);

internal sealed record RenderOutput(
    string Text, string Background, string Foreground,
    IReadOnlyList<RenderItem> Left, IReadOnlyList<RenderItem> Right,
    int Height, double FontSize, string FullScreenBehavior, IReadOnlyList<string> Warnings);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RenderOutput))]
internal sealed partial class RenderJsonContext : JsonSerializerContext;
