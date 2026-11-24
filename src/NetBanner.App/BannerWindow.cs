using Avalonia;
using Avalonia.Controls;
using NetBanner.Core;
using NetBanner.UI;
using NetBanner.Windows;

namespace NetBanner.App;

/// <summary>The banner strip on one monitor.</summary>
internal sealed class BannerWindow : Window
{
    private readonly MonitorInfo _monitor;
    private readonly BannerStrip _strip = new();
    private BannerSettings _settings;
    private AppBar? _appBar;
    private bool _allowClose;

    /// <summary>Raised (on the UI thread) when monitors or DPI change.</summary>
    public event Action? DisplayChanged;

    public BannerWindow(MonitorInfo monitor, BannerSettings settings)
    {
        _monitor = monitor;
        _settings = settings;

        Title = "NetBanner";
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // Create the window on its own monitor, so it starts at that monitor's DPI.
        Position = new PixelPoint(monitor.Bounds.X, monitor.Bounds.Y);
        SizeToContent = SizeToContent.Manual;
        MinWidth = 0;
        MinHeight = 0;
        Width = monitor.Bounds.Width / monitor.Scale;
        Height = settings.Height;

        Content = _strip;
        _strip.Render(settings, Tokens.FromSystem);
        Opened += OnOpened;
    }

    private int HeightPx => (int)Math.Round(_settings.Height * _monitor.Scale);

    private void OnOpened(object? sender, EventArgs e)
    {
        var hwnd = TryGetPlatformHandle()?.Handle ?? 0;
        if (hwnd == 0)
        {
            Log.Error("Could not get the banner's native window handle; it cannot reserve screen space.");
            return;
        }

        _appBar = new AppBar(hwnd, _monitor.Bounds, HeightPx, _settings.FullScreenBehavior);
        _appBar.DisplayChanged += () => DisplayChanged?.Invoke();
        _appBar.Register();
    }

    public void Apply(BannerSettings settings)
    {
        _settings = settings;
        Height = settings.Height;
        _strip.Render(settings, Tokens.FromSystem);
        _appBar?.Update(HeightPx, settings.FullScreenBehavior);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Users can't close the banner (e.g. Alt+F4). Only the app can, and Windows when signing out or shutting
        // down: refusing that would block sign-out ("This app is preventing you from signing out").
        bool endingSession = e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown;
        if (!_allowClose && !endingSession)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    public void ForceClose()
    {
        _allowClose = true;
        _appBar?.Dispose();
        _appBar = null;
        Close();
    }
}
