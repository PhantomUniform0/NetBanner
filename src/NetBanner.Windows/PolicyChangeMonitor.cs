using Microsoft.Win32;
using NetBanner.Core;

namespace NetBanner.Windows;

/// <summary>
/// Raises <see cref="Changed"/> after a machine Group Policy refresh, or after any change under
/// <c>HKLM\SOFTWARE\Policies</c> (which covers both the new and the legacy keys, and manual edits).
/// Bursts of changes are debounced into one event. Raised on a background thread.
/// </summary>
public sealed class PolicyChangeMonitor : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(750);

    private readonly AutoResetEvent _gpEvent = new(false);
    private readonly AutoResetEvent _regEvent = new(false);
    private readonly ManualResetEvent _stop = new(false);
    private Thread? _thread;
    private bool _gpRegistered;

    public event EventHandler? Changed;

    public void Start()
    {
        _gpRegistered = Native.RegisterGPNotification(_gpEvent.SafeWaitHandle, bMachine: true);
        if (!_gpRegistered)
        {
            Log.Warn("RegisterGPNotification failed; relying on registry change notification only.");
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "NetBanner policy monitor" };
        _thread.Start();
    }

    private void Run()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var policies = hklm.OpenSubKey(@"SOFTWARE\Policies");
            WaitHandle[] handles = [_stop, _gpEvent, _regEvent];

            while (true)
            {
                if (policies is not null)
                {
                    int rc = Native.RegNotifyChangeKeyValue(policies.Handle, true,
                        Native.REG_NOTIFY_CHANGE_NAME | Native.REG_NOTIFY_CHANGE_LAST_SET, _regEvent.SafeWaitHandle, true);
                    if (rc != 0)
                    {
                        Log.Warn($"RegNotifyChangeKeyValue failed ({rc}).");
                    }
                }

                if (WaitHandle.WaitAny(handles) == 0)
                {
                    return;
                }

                // Let a GP refresh finish writing before reading. Keep extending while signals arrive.
                while (true)
                {
                    int idx = WaitHandle.WaitAny(handles, Debounce);
                    if (idx == 0)
                    {
                        return;
                    }

                    if (idx == WaitHandle.WaitTimeout)
                    {
                        break;
                    }

                    // The registry watch is one-shot, so re-arm it before waiting again.
                    if (idx == 2 && policies is not null)
                    {
                        Native.RegNotifyChangeKeyValue(policies.Handle, true,
                            Native.REG_NOTIFY_CHANGE_NAME | Native.REG_NOTIFY_CHANGE_LAST_SET, _regEvent.SafeWaitHandle, true);
                    }
                }

                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Policy change monitor stopped unexpectedly. Live policy updates are disabled until restart.", ex);
        }
    }

    public void Dispose()
    {
        _stop.Set();
        _thread?.Join(TimeSpan.FromSeconds(2));
        if (_gpRegistered)
        {
            Native.UnregisterGPNotification(_gpEvent.SafeWaitHandle);
        }

        _gpEvent.Dispose();
        _regEvent.Dispose();
        _stop.Dispose();
    }
}
