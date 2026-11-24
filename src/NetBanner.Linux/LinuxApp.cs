using System.Net.NetworkInformation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using NetBanner.Core;
using NetBanner.UI;

namespace NetBanner.Linux;

/// <summary>The X11 banner: one dock window per monitor, rebuilt when monitors or settings change.</summary>
internal sealed class LinuxApp : Application
{
    private readonly List<LinuxBannerWindow> _windows = [];
    private ConfigFileWatcher? _watcher;
    private ConfigFileWatcher? _serverWatcher;
    private DispatcherTimer? _rebuildTimer;
    private Window? _probe;
    private BannerSettings _settings = BannerSettings.Default;
    private string _layout = string.Empty;

    /// <summary>Monitor bounds and scaling only (not work areas), as a comparable string.</summary>
    private string MonitorLayout() =>
        string.Join(';', _probe!.Screens.All.Select(s => $"{s.Bounds.X},{s.Bounds.Y},{s.Bounds.Width},{s.Bounds.Height}@{s.Scaling}"));

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Light;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        LoadSettings();

        // Screens are reached through a window; this one is never shown.
        _probe = new Window();
        // Only rebuild when monitors really change. Screens.Changed also fires when the work area changes, which our
        // own strut causes: rebuilding on that removes and re-adds the strut, and the banner flaps forever.
        _probe.Screens.Changed += (_, _) =>
        {
            if (MonitorLayout() != _layout)
            {
                ScheduleRebuild();
            }
        };

        _rebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _rebuildTimer.Tick += (_, _) =>
        {
            _rebuildTimer.Stop();
            Rebuild();
        };

        // The local file, and the server config that root's sync service writes.
        _watcher = new ConfigFileWatcher(Program.ConfigPath);
        _watcher.Changed += (_, _) => Dispatcher.UIThread.Post(() => Reload("Config file changed"));
        _watcher.Start();
        _serverWatcher = new ConfigFileWatcher(LinuxSettings.ServerCachePath);
        _serverWatcher.Changed += (_, _) => Dispatcher.UIThread.Post(() => Reload("Server config changed"));
        _serverWatcher.Start();
        NetworkChange.NetworkAddressChanged += (_, _) => Dispatcher.UIThread.Post(() => _windows.ForEach(w => w.Render(_settings)));

        Rebuild();
        base.OnFrameworkInitializationCompleted();
    }

    private void Reload(string reason)
    {
        Log.Info($"{reason}; reloading.");
        int oldHeight = _settings.Height;
        LoadSettings();
        if (_settings.Height != oldHeight)
        {
            Rebuild();
        }
        else
        {
            _windows.ForEach(w => w.Render(_settings));
        }
    }

    private void LoadSettings()
    {
        var warnings = new List<string>();
        _settings = LinuxSettings.Load(Program.ConfigPath, warnings);
        warnings.ForEach(Log.Warn);
    }

    private void ScheduleRebuild()
    {
        _rebuildTimer?.Stop();
        _rebuildTimer?.Start();
    }

    private void Rebuild()
    {
        foreach (var w in _windows)
        {
            w.ForceClose();
        }

        _windows.Clear();
        _layout = MonitorLayout();
        var screens = _probe!.Screens.All;
        if (screens.Count == 0)
        {
            Log.Warn("No screens found.");
            return;
        }

        foreach (var screen in screens)
        {
            var window = new LinuxBannerWindow(screen, _settings);
            _windows.Add(window);
            window.Show();
        }

        Log.Info($"Showing the banner on {screens.Count} monitor(s).");
    }
}

/// <summary>The banner on one monitor.</summary>
internal sealed class LinuxBannerWindow : Window
{
    private readonly BannerStrip _strip = new(centerOverflowingText: true);
    private bool _allowClose;

    public LinuxBannerWindow(Screen screen, BannerSettings settings)
    {
        Title = "NetBanner";
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = settings.FullScreenBehavior == FullScreenBehavior.StayOnTop;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.Manual;
        MinWidth = 0;
        MinHeight = 0;

        var b = screen.Bounds;
        Position = new PixelPoint(b.X, b.Y);
        Width = b.Width / screen.Scaling;
        Height = settings.Height;
        Content = _strip;
        Render(settings);

        // Dock type and strut must be set before the window is first shown.
        int heightPx = (int)Math.Round(settings.Height * screen.Scaling);
        if (TryGetPlatformHandle()?.Handle is { } xid and not 0)
        {
            X11Dock.Apply(xid, (b.X, b.Y, b.Width, b.Height), heightPx, Topmost);
        }
        else
        {
            Log.Error("No X11 window handle; the banner cannot reserve screen space (is this an X11/XWayland session?).");
        }
    }

    public void Render(BannerSettings settings) => _strip.Render(settings, Tokens.FromSystem);

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Users can't close it; the session ending can.
        if (!_allowClose && e.CloseReason is not (WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown))
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}
