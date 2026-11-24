using System.Runtime.InteropServices;
using NetBanner.Core;

namespace NetBanner.Linux;

/// <summary>
/// Marks a window as an EWMH dock with a top strut, which is how X11 panels reserve screen space. KWin honours this
/// for X11 windows on both X11 and Wayland (XWayland) sessions, so maximized windows stop below the banner.
/// Properties must be set before the window is mapped (shown).
/// </summary>
internal static partial class X11Dock
{
    private const string Lib = "libX11.so.6";
    private const nint XA_ATOM = 4, XA_CARDINAL = 6;
    private const int PropModeReplace = 0;

    [LibraryImport(Lib)]
    private static partial nint XOpenDisplay(nint name);

    [LibraryImport(Lib)]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint XInternAtom(nint display, string name, int onlyIfExists);

    [LibraryImport(Lib)]
    private static partial int XChangeProperty(nint display, nint window, nint property, nint type, int format, int mode, nint[] data, int count);

    [LibraryImport(Lib)]
    private static partial int XFlush(nint display);

    /// <param name="window">The X11 window id (Avalonia's platform handle).</param>
    /// <param name="monitor">The monitor's bounds in root-window pixels.</param>
    /// <param name="heightPx">Banner height in pixels.</param>
    /// <param name="stayOnTop">Keep above other windows (and full-screen apps, where the window manager allows it).</param>
    public static void Apply(nint window, (int X, int Y, int Width, int Height) monitor, int heightPx, bool stayOnTop)
    {
        var display = XOpenDisplay(0);
        if (display == 0)
        {
            Log.Error("Cannot open the X display; the banner cannot reserve screen space.");
            return;
        }

        try
        {
            nint Atom(string name) => XInternAtom(display, name, 0);
            void Set(string prop, nint type, params nint[] values) =>
                XChangeProperty(display, window, Atom(prop), type, 32, PropModeReplace, values, values.Length);

            Set("_NET_WM_WINDOW_TYPE", XA_ATOM, Atom("_NET_WM_WINDOW_TYPE_DOCK"));

            var state = new List<nint> { Atom("_NET_WM_STATE_STICKY"), Atom("_NET_WM_STATE_SKIP_TASKBAR"), Atom("_NET_WM_STATE_SKIP_PAGER") };
            if (stayOnTop)
            {
                state.Add(Atom("_NET_WM_STATE_ABOVE"));
            }

            Set("_NET_WM_STATE", XA_ATOM, [.. state]);
            Set("_NET_WM_DESKTOP", XA_CARDINAL, unchecked((nint)0xFFFFFFFF)); // all desktops

            // Struts are measured from the edge of the whole X screen, so only a monitor whose top edge is the
            // screen's top edge can reserve space at its top.
            if (monitor.Y == 0)
            {
                nint top = heightPx;
                nint startX = monitor.X, endX = monitor.X + monitor.Width - 1;
                Set("_NET_WM_STRUT_PARTIAL", XA_CARDINAL, 0, 0, top, 0, 0, 0, 0, 0, startX, endX, 0, 0);
                Set("_NET_WM_STRUT", XA_CARDINAL, 0, 0, top, 0);
            }
            else
            {
                Log.Warn($"Monitor at {monitor.X},{monitor.Y} is not at the top of the screen layout; its banner cannot reserve space.");
            }

            XFlush(display);
        }
        finally
        {
            XCloseDisplay(display);
        }
    }
}
