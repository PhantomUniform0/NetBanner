using System.Globalization;
using Microsoft.Win32;
using NetBanner.Core;

namespace NetBanner.Windows;

/// <summary>Reads the NetBanner 3 GPO key (<c>HKLM\SOFTWARE\Policies\NetBanner</c>).</summary>
public static class PolicySource
{
    public const string KeyPath = @"SOFTWARE\Policies\NetBanner";

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

            return PolicyMapper.Map(values,
                ReadList(key, PolicyMapper.LeftItemsSubkey),
                ReadList(key, PolicyMapper.RightItemsSubkey));
        }
        catch (Exception ex)
        {
            Log.Error($@"Failed to read HKLM\{KeyPath}.", ex);
            return null;
        }
    }

    /// <summary>Reads an ADMX list (values named "1", "2", ...), in numeric order.</summary>
    private static List<string>? ReadList(RegistryKey parent, string subkey)
    {
        using var key = parent.OpenSubKey(subkey);
        if (key is null)
        {
            return null;
        }

        return key.GetValueNames()
            .Select(n => (Name: n, Order: int.TryParse(n, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : int.MaxValue))
            .OrderBy(x => x.Order).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => key.GetValue(x.Name)?.ToString())
            .OfType<string>()
            .ToList();
    }
}
