using System.Security.AccessControl;
using System.Security.Principal;
using NetBanner.Core;

namespace NetBanner.Windows;

/// <summary>
/// Replaces this process's DACL so standard users can't terminate it (e.g. from Task Manager).
/// SYSTEM and elevated Administrators keep full access. Like the legacy app, this is a deterrent:
/// the process owner could still change the DACL with dedicated tools.
/// </summary>
public static class ProcessHardener
{
    // PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE | READ_CONTROL
    private const int UserRights = 0x1000 | 0x100000 | 0x20000;

    public static bool Apply()
    {
        try
        {
            var user = WindowsIdentity.GetCurrent().User
                ?? throw new InvalidOperationException("No user SID for the current process.");

            // P = protected DACL. GA = generic all.
            var sddl = $"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;0x{UserRights:X};;;{user.Value})";
            var sd = new RawSecurityDescriptor(sddl);
            var bytes = new byte[sd.BinaryLength];
            sd.GetBinaryForm(bytes, 0);

            if (!Native.SetKernelObjectSecurity(Native.GetCurrentProcess(), Native.DACL_SECURITY_INFORMATION, bytes))
            {
                Log.Warn($"SetKernelObjectSecurity failed (error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}).");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to harden process.", ex);
            return false;
        }
    }
}
