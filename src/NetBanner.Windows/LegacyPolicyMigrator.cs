using System.Globalization;
using System.Text;
using Microsoft.Win32;
using NetBanner.Core;

namespace NetBanner.Windows;

/// <summary>
/// Migrates legacy NetBanner settings in the machine's <b>local</b> Group Policy to NetBanner 3 policies.
/// Domain GPO settings never appear in the local Registry.pol, so they are left alone (and only reported).
/// </summary>
public static class LegacyPolicyMigrator
{
    private static string GpoDir => Path.Combine(Environment.SystemDirectory, "GroupPolicy");
    private static string PolPath => Path.Combine(GpoDir, "Machine", "Registry.pol");

    /// <returns>Exit code (0 = success or nothing to do) and a one-line summary.</returns>
    public static (int Code, string Output) Run()
    {
        try
        {
            var (code, output) = MigrateLocal();
            if (LegacyStillSet())
            {
                output += " Legacy NetBanner policy values are still set from a domain GPO: migrate that GPO to the" +
                          " NetBanner 3 policies in GPMC. They keep working until then.";
            }

            return (code, output);
        }
        catch (Exception ex)
        {
            return (1, $"Migration failed, nothing changed: {ex.Message}");
        }
    }

    private static (int, string) MigrateLocal()
    {
        if (!File.Exists(PolPath))
        {
            return (0, "No local Group Policy registry settings.");
        }

        var original = File.ReadAllBytes(PolPath);
        var result = LegacyPolicyMigration.Migrate(RegistryPol.Parse(original));
        if (!result.Changed)
        {
            return (0, "No legacy NetBanner settings in local Group Policy.");
        }

        // Backup first, then write the new file in one step.
        var backup = PolPath + ".netbanner3-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        File.Copy(PolPath, backup, overwrite: true);
        var temp = PolPath + ".netbanner3-tmp";
        File.WriteAllBytes(temp, RegistryPol.Serialize(result.Entries));
        File.Move(temp, PolPath, overwrite: true);
        BumpGptVersion();

        // Apply to the live registry now (new values first, so the banner never loses its marking), instead of
        // waiting for the next policy refresh. The refresh then finds the registry already matching.
        ApplyLive(result);

        var sb = new StringBuilder("Migrated local legacy NetBanner policy to NetBanner 3. Added: ");
        sb.Append(string.Join(", ", result.Added.Where(a => !a.IsDirective).Select(Describe)));
        sb.Append(". Removed ").Append(result.Removed.Count).Append(" legacy entries. Backup: ").Append(backup).Append('.');
        foreach (var n in result.Notes)
        {
            sb.Append(' ').Append(n);
        }

        return (0, sb.ToString());
    }

    private static string Describe(PolEntry e)
    {
        var name = e.Key.EndsWith(PolicyMapper.LeftItemsSubkey, StringComparison.OrdinalIgnoreCase) ? "LeftItems\\" + e.ValueName
            : e.Key.EndsWith(PolicyMapper.RightItemsSubkey, StringComparison.OrdinalIgnoreCase) ? "RightItems\\" + e.ValueName
            : e.ValueName;
        return $"{name}={e.Value}";
    }

    private static void ApplyLive(LegacyPolicyMigration.Result result)
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        foreach (var e in result.Added.Where(a => !a.IsDirective))
        {
            using var key = hklm.CreateSubKey(e.Key.Trim('\\'));
            if (e.Type == PolEntry.RegDword)
            {
                key.SetValue(e.ValueName, (int)e.Value, RegistryValueKind.DWord);
            }
            else
            {
                key.SetValue(e.ValueName, (string)e.Value, RegistryValueKind.String);
            }
        }

        foreach (var e in result.Removed.Where(r => !r.IsDirective))
        {
            using var key = hklm.OpenSubKey(e.Key.Trim('\\'), writable: true);
            key?.DeleteValue(e.ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>The machine half of gpt.ini's Version is its low 16 bits; bumping it makes Windows reprocess the file.</summary>
    private static void BumpGptVersion()
    {
        var gpt = Path.Combine(GpoDir, "gpt.ini");
        if (!File.Exists(gpt))
        {
            return;
        }

        var lines = File.ReadAllLines(gpt).ToList();
        int i = lines.FindIndex(l => l.TrimStart().StartsWith("Version=", StringComparison.OrdinalIgnoreCase));
        if (i < 0 || !uint.TryParse(lines[i].Split('=', 2)[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            return;
        }

        uint machine = ((version & 0xFFFF) + 1) & 0xFFFF;
        lines[i] = "Version=" + ((version & 0xFFFF0000) | machine).ToString(CultureInfo.InvariantCulture);
        File.WriteAllLines(gpt, lines);
    }

    private static bool LegacyStillSet()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hklm.OpenSubKey(LegacyPolicySource.KeyPath);
        return key is not null && key.GetValueNames().Any(n => n.Length > 0);
    }
}
