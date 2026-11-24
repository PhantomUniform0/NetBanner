using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NetBanner.Windows;

/// <summary>
/// Intercepts messages for a window (via <c>SetWindowSubclass</c>) without depending on the UI framework.
/// The handler returns true to mark a message handled; otherwise the message goes on to the original window procedure.
/// </summary>
public sealed unsafe class WindowHook : IDisposable
{
    public delegate bool Handler(nint hwnd, uint msg, nint wParam, nint lParam, out nint result);

    private const nuint SubclassId = 0x4E42; // "NB"
    private readonly nint _hwnd;
    private readonly Handler _handler;
    private GCHandle _self;
    private bool _attached;

    public WindowHook(nint hwnd, Handler handler)
    {
        _hwnd = hwnd;
        _handler = handler;
        _self = GCHandle.Alloc(this);
        _attached = Native.SetWindowSubclass(hwnd, &SubclassProc, SubclassId, (nuint)(nint)GCHandle.ToIntPtr(_self));
        if (!_attached)
        {
            _self.Free();
            throw new InvalidOperationException("SetWindowSubclass failed.");
        }
    }

    public void Dispose() => Detach();

    private void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        Native.RemoveWindowSubclass(_hwnd, &SubclassProc, SubclassId);
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
    {
        if (GCHandle.FromIntPtr((nint)refData).Target is not WindowHook hook)
        {
            return Native.DefSubclassProc(hwnd, msg, wParam, lParam);
        }

        if (msg == Native.WM_NCDESTROY)
        {
            var r = Native.DefSubclassProc(hwnd, msg, wParam, lParam);
            hook.Detach();
            return r;
        }

        try
        {
            if (hook._handler(hwnd, msg, wParam, lParam, out var result))
            {
                return result;
            }
        }
        catch (Exception ex)
        {
            // Exceptions must not escape into native code.
            Core.Log.Error("Unhandled error in window message hook.", ex);
        }

        return Native.DefSubclassProc(hwnd, msg, wParam, lParam);
    }
}
