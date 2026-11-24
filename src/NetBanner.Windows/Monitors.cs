using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NetBanner.Windows;

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height);

/// <param name="Scale">DPI scale factor (1.0 = 96 DPI).</param>
public sealed record MonitorInfo(ScreenRect Bounds, double Scale, bool IsPrimary);

public static unsafe class Monitors
{
    /// <summary>Lists all monitors in physical pixels. Requires a per-monitor DPI-aware process.</summary>
    public static IReadOnlyList<MonitorInfo> GetAll()
    {
        var list = new List<MonitorInfo>();
        var handle = GCHandle.Alloc(list);
        try
        {
            Native.EnumDisplayMonitors(0, 0, &EnumProc, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        // Primary first, then left-to-right, so the order is stable.
        return list.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ToList();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int EnumProc(nint hMonitor, nint hdc, Native.RECT* rect, nint data)
    {
        var list = (List<MonitorInfo>)GCHandle.FromIntPtr(data).Target!;
        var info = new Native.MONITORINFO { cbSize = (uint)sizeof(Native.MONITORINFO) };
        if (Native.GetMonitorInfo(hMonitor, ref info))
        {
            double scale = Native.GetDpiForMonitor(hMonitor, Native.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0
                ? dpiX / 96.0
                : 1.0;
            var r = info.rcMonitor;
            list.Add(new MonitorInfo(
                new ScreenRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top),
                scale,
                (info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0));
        }

        return 1; // continue enumeration
    }
}
