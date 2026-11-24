using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace NetBanner.Config;

internal static class Program
{
    public static string[] Args { get; private set; } = [];

    [STAThread]
    public static int Main(string[] args)
    {
        Args = args;
        if (TryRelaunchElevated(args))
        {
            return 0;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Saving the default config needs admin rights, so administrators get a UAC prompt on launch.
    /// If they decline, or the user isn't an administrator, the tool runs read-only instead.
    /// </summary>
    private static bool TryRelaunchElevated(string[] args)
    {
        if (ConfigOptions.Parse(args).IsDefaultPath is false || !Windows.ConfigStore.IsLimitedAdmin())
        {
            return false;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            System.Diagnostics.Process.Start(psi);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // UAC prompt declined
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ConfigApp>()
            .UsePlatformDetect()
            .LogToTrace();
}

/// <summary>Command-line switches for the config tool.</summary>
internal sealed record ConfigOptions(string ConfigPath, bool IsDefaultPath, bool NoPolicy)
{
    public static ConfigOptions Parse(string[] args)
    {
        string? path = null;
        bool noPolicy = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--config" when i + 1 < args.Length:
                    path = args[++i];
                    break;
                case "--no-policy":
                    noPolicy = true;
                    break;
            }
        }

        return new ConfigOptions(path ?? Windows.ConfigStore.DefaultPath, path is null, noPolicy);
    }
}

internal sealed class ConfigApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Light;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(ConfigOptions.Parse(Program.Args));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
