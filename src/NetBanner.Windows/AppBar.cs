using System.Runtime.InteropServices;
using NetBanner.Core;

namespace NetBanner.Windows;

/// <summary>
/// Docks a window to the top of one monitor as a shell AppBar, so Windows shrinks the work area and
/// maximized windows don't cover it. Also handles Explorer restarts and full-screen apps.
/// </summary>
public sealed class AppBar : IDisposable
{
    private static readonly uint CallbackMessage = Native.RegisterWindowMessage("NetBanner.AppBarCallback");
    private static readonly uint TaskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");

    private readonly nint _hwnd;
    private readonly ScreenRect _monitor;
    private readonly WindowHook _hook;
    private int _heightPx;
    private FullScreenBehavior _fullScreen;
    private bool _registered;
    private Native.RECT? _rect;

    // Explorer-less fallback (Server Core, custom shells): the work area we replaced, so it can be put back.
    private Native.RECT? _originalWorkArea;
    private bool _yielded;
    private bool _disposed;

    /// <summary>Raised when the display configuration or DPI changes. The owner should rebuild banners.</summary>
    public event Action? DisplayChanged;

    public AppBar(nint hwnd, ScreenRect monitor, int heightPx, FullScreenBehavior fullScreen)
    {
        // UI frameworks may hand out a child (content) window handle. AppBars must be top-level.
        var root = Native.GetAncestor(hwnd, Native.GA_ROOT);
        if (root != 0 && root != hwnd)
        {
            Log.Info($"Banner handle 0x{hwnd:X} is a child window; using top-level 0x{root:X}.");
            hwnd = root;
        }

        _hwnd = hwnd;
        _monitor = monitor;
        _heightPx = heightPx;
        _fullScreen = fullScreen;
        ApplyBannerStyles(hwnd);
        _hook = new WindowHook(hwnd, OnMessage);
    }

    public void Register()
    {
        var abd = NewData();
        abd.uCallbackMessage = CallbackMessage;
        _registered = Native.SHAppBarMessage(Native.ABM_NEW, ref abd) != 0;
        if (_registered)
        {
            RestoreWorkArea();
        }
        else
        {
            Log.Info("No shell AppBar support (Explorer not running, e.g. Server Core). Reserving space by adjusting the work area instead.");
        }

        Reposition();
    }

    /// <summary>
    /// Fallback without Explorer: shrink the monitor's work area so maximized windows start below the banner.
    /// The original work area is saved and put back on exit, or when Explorer starts and AppBars work again.
    /// </summary>
    private void ReserveWorkArea(Native.RECT banner)
    {
        var mon = Native.MonitorFromWindow(_hwnd, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(mon, ref info))
        {
            return;
        }

        _originalWorkArea ??= info.rcWork;
        var work = _originalWorkArea.Value;
        work.Top = Math.Max(work.Top, banner.Bottom);
        if (work.Top == info.rcWork.Top && work.Bottom == info.rcWork.Bottom
            && work.Left == info.rcWork.Left && work.Right == info.rcWork.Right)
        {
            return;
        }

        if (!Native.SystemParametersInfo(Native.SPI_SETWORKAREA, 0, ref work, Native.SPIF_SENDCHANGE))
        {
            Log.Warn($"SPI_SETWORKAREA failed (error {Marshal.GetLastPInvokeError()}); the banner can be covered by maximized windows.");
        }
    }

    private void RestoreWorkArea()
    {
        if (_originalWorkArea is { } original)
        {
            Native.SystemParametersInfo(Native.SPI_SETWORKAREA, 0, ref original, Native.SPIF_SENDCHANGE);
            _originalWorkArea = null;
        }
    }

    public void Update(int heightPx, FullScreenBehavior fullScreen)
    {
        _fullScreen = fullScreen;
        if (fullScreen == FullScreenBehavior.StayOnTop && _yielded)
        {
            Restore();
        }

        if (heightPx != _heightPx)
        {
            _heightPx = heightPx;
            Reposition();
        }
    }

    private void Reposition()
    {
        var rc = new Native.RECT
        {
            Left = _monitor.X,
            Top = _monitor.Y,
            Right = _monitor.X + _monitor.Width,
            Bottom = _monitor.Y + _heightPx,
        };

        if (_registered)
        {
            var abd = NewData();
            abd.uEdge = Native.ABE_TOP;
            abd.rc = rc;
            Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref abd);
            // The shell may push us down (e.g. below a top-docked taskbar). Keep our height.
            abd.rc.Bottom = abd.rc.Top + _heightPx;
            Native.SHAppBarMessage(Native.ABM_SETPOS, ref abd);
            rc = abd.rc;
        }
        else
        {
            ReserveWorkArea(rc);
        }

        _rect = rc;

        // Moving the window makes the shell send ABN_POSCHANGED to every AppBar. Only move when
        // needed, so several banners don't keep repositioning each other.
        Native.GetWindowRect(_hwnd, out var current);
        if (current.Left == rc.Left && current.Top == rc.Top && current.Right == rc.Right && current.Bottom == rc.Bottom)
        {
            return;
        }

        Native.SetWindowPos(_hwnd, _yielded ? Native.HWND_BOTTOM : Native.HWND_TOPMOST,
            rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

        Native.GetWindowRect(_hwnd, out var actual);
        if (actual.Bottom - actual.Top != rc.Bottom - rc.Top)
        {
            Log.Warn($"Banner window is {actual.Bottom - actual.Top}px tall; expected {rc.Bottom - rc.Top}px.");
        }
    }

    private bool OnMessage(nint hwnd, uint msg, nint wParam, nint lParam, out nint result)
    {
        result = 0;

        if (msg == CallbackMessage && CallbackMessage != 0)
        {
            switch ((int)wParam)
            {
                case Native.ABN_POSCHANGED:
                    Reposition();
                    break;
                case Native.ABN_FULLSCREENAPP:
                    OnFullScreenApp(lParam != 0);
                    break;
            }

            return true;
        }

        if (msg == TaskbarCreated && TaskbarCreated != 0)
        {
            // Explorer restarted, so every AppBar registration was lost.
            Log.Info("Explorer restarted; re-registering banner AppBar.");
            _registered = false;
            Register();
            return false;
        }

        switch (msg)
        {
            case Native.WM_WINDOWPOSCHANGING when _rect is { } r:
                // The reserved strip is authoritative. Let the UI framework handle the message first
                // (it applies its own minimum size), then pin the window to the strip, so nothing
                // (the framework, the user, other apps) can move or resize it.
                result = Native.DefSubclassProc(hwnd, msg, wParam, lParam);
                unsafe
                {
                    var wp = (Native.WINDOWPOS*)lParam;
                    wp->x = r.Left;
                    wp->y = r.Top;
                    wp->cx = r.Right - r.Left;
                    wp->cy = r.Bottom - r.Top;
                    wp->flags &= ~(Native.SWP_NOMOVE | Native.SWP_NOSIZE);
                }

                return true;
            case Native.WM_GETMINMAXINFO:
                // Windows enforces a ~38px minimum height on resizable windows. Allow our thin strip.
                unsafe
                {
                    var mmi = (int*)lParam;
                    mmi[6] = 1; // ptMinTrackSize.x
                    mmi[7] = 1; // ptMinTrackSize.y
                }

                return true;
            case Native.WM_MOUSEACTIVATE:
                result = Native.MA_NOACTIVATE;
                return true;
            case Native.WM_QUERYENDSESSION:
                // Never block sign-out or shutdown. Answered here so the UI framework's close handling
                // (which refuses user closes) can't veto it.
                result = 1;
                return true;
            case Native.WM_ENDSESSION when wParam != 0:
                RestoreWorkArea();
                return false;
            case Native.WM_ACTIVATE when _registered:
                SendSimple(Native.ABM_ACTIVATE);
                return false;
            case Native.WM_WINDOWPOSCHANGED when _registered:
                SendSimple(Native.ABM_WINDOWPOSCHANGED);
                return false;
            case Native.WM_DISPLAYCHANGE:
            case Native.WM_DPICHANGED:
                DisplayChanged?.Invoke();
                return false;
        }

        return false;
    }

    private void OnFullScreenApp(bool opening)
    {
        if (opening)
        {
            bool onOurMonitor = Native.MonitorFromWindow(Native.GetForegroundWindow(), Native.MONITOR_DEFAULTTONEAREST)
                == Native.MonitorFromWindow(_hwnd, Native.MONITOR_DEFAULTTONEAREST);

            if (_fullScreen == FullScreenBehavior.Yield && onOurMonitor)
            {
                _yielded = true;
                Native.SetWindowPos(_hwnd, Native.HWND_NOTOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
            else
            {
                Restore();
            }
        }
        else
        {
            Restore();
        }
    }

    private void Restore()
    {
        _yielded = false;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private void SendSimple(uint message)
    {
        var abd = NewData();
        Native.SHAppBarMessage(message, ref abd);
    }

    private Native.APPBARDATA NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<Native.APPBARDATA>(),
        hWnd = _hwnd,
    };

    /// <summary>Removes the window from Alt+Tab and the taskbar, stops it taking focus, and turns off Win11 rounded corners.</summary>
    private static void ApplyBannerStyles(nint hwnd)
    {
        long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
        ex &= ~Native.WS_EX_APPWINDOW;
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, (nint)ex);

        int noRound = Native.DWMWCP_DONOTROUND;
        _ = Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref noRound, sizeof(int));

        Native.SetWindowPos(hwnd, 0, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_registered)
        {
            SendSimple(Native.ABM_REMOVE);
            _registered = false;
        }

        RestoreWorkArea();
        _hook.Dispose();
    }
}
