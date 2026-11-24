using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using NetBanner.Core;
using NetBanner.Windows;

namespace NetBanner.App;

internal sealed class App : Application
{
    public static AppOptions Options { get; set; } = new();

    private SettingsService? _settings;
    private BannerManager? _banners;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Light;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _settings = new SettingsService(Options);
            _banners = new BannerManager(_settings.Current);
            _settings.Changed += s => Dispatcher.UIThread.Post(() => _banners?.Apply(s));

            _banners.Start();
            _settings.Start();

            if (!Options.NoHarden)
            {
                ProcessHardener.Apply();
            }

            if (Options.ExitAfterSeconds is { } secs)
            {
                DispatcherTimer.RunOnce(() => desktop.Shutdown(), TimeSpan.FromSeconds(secs));
            }

            desktop.Exit += (_, _) => Shutdown();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void Shutdown()
    {
        Log.Info("NetBanner exiting.");
        _settings?.Dispose();
        _banners?.Dispose();
        _settings = null;
        _banners = null;
    }
}
