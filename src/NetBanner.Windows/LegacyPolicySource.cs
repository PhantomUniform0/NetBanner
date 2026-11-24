using Microsoft.Win32;
using NetBanner.Core;

namespace NetBanner.Windows;

/// <summary>Reads the legacy NetBanner GPO key so existing GPOs keep working.</summary>
public static class LegacyPolicySource
{
    public const string KeyPath = @"SOFTWARE\Policies\Microsoft\NetBanner";

    public static PartialSettings? Read()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(KeyPath);
            if (key is null)
            {
                return null;
            }

            var values = key.GetValueNames()
                .Where(n => n.Length > 0)
                .ToDictionary(n => n, n => key.GetValue(n), StringComparer.OrdinalIgnoreCase);
            return LegacyPolicyMapper.Map(values);
        }
        catch (Exception ex)
        {
            Log.Error($@"Failed to read HKLM\{KeyPath}.", ex);
            return null;
        }
    }
}
