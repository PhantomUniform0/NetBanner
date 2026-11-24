using Avalonia.Threading;
using NetBanner.Core;
using NetBanner.Windows;

namespace NetBanner.App;

/// <summary>Keeps one <see cref="BannerWindow"/> per monitor and rebuilds them when displays change.</summary>
internal sealed class BannerManager : IDisposable
{
    private readonly List<BannerWindow> _windows = [];
    private readonly DispatcherTimer _rebuildTimer;
    private BannerSettings _settings;
    private bool _disposed;

    public BannerManager(BannerSettings settings)
    {
        _settings = settings;
        // Displays change in bursts (hotplug, resolution, DPI), so wait for them to settle.
        _rebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _rebuildTimer.Tick += (_, _) =>
        {
            _rebuildTimer.Stop();
            Rebuild();
        };
    }

    public void Start() => Rebuild();

    public void Apply(BannerSettings settings)
    {
        Dispatcher.UIThread.VerifyAccess();
        bool layoutChanged = settings.Height != _settings.Height;
        _settings = settings;
        if (layoutChanged)
        {
            Rebuild();
            return;
        }

        foreach (var w in _windows)
        {
            w.Apply(settings);
        }
    }

    private void RequestRebuild()
    {
        if (_disposed)
        {
            return;
        }

        _rebuildTimer.Stop();
        _rebuildTimer.Start();
    }

    private void Rebuild()
    {
        if (_disposed)
        {
            return;
        }

        CloseAll();
        var monitors = Monitors.GetAll();
        Log.Info($"Showing banner on {monitors.Count} monitor(s): " +
                 string.Join("; ", monitors.Select(m => $"{m.Bounds.Width}x{m.Bounds.Height}@{m.Bounds.X},{m.Bounds.Y} scale {m.Scale:0.##}")));

        foreach (var monitor in monitors)
        {
            var w = new BannerWindow(monitor, _settings);
            w.DisplayChanged += RequestRebuild;
            _windows.Add(w);
            w.Show();
        }
    }

    private void CloseAll()
    {
        foreach (var w in _windows)
        {
            w.DisplayChanged -= RequestRebuild;
            w.ForceClose();
        }

        _windows.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _rebuildTimer.Stop();
        CloseAll();
    }
}
