using System.Globalization;

namespace NetBanner.Core;

/// <summary>
/// Moves legacy NetBanner policy settings in a <c>Registry.pol</c> to the equivalent NetBanner 3 policies, so they show
/// (and can be edited) under "NetBanner 3" in gpedit. OS-independent; the caller reads and writes the file.
/// </summary>
/// <remarks>
/// A setting is only migrated where NetBanner 3 policy doesn't already set it, because NetBanner 3 policy wins anyway
/// and must not be overwritten. The banner shows the same thing before and after.
/// </remarks>
public static class LegacyPolicyMigration
{
    public const string LegacyKey = @"Software\Policies\Microsoft\NetBanner";
    public const string NewKey = @"Software\Policies\NetBanner";

    public sealed record Result(
        IReadOnlyList<PolEntry> Entries,
        IReadOnlyList<PolEntry> Added,
        IReadOnlyList<PolEntry> Removed,
        IReadOnlyList<string> Notes)
    {
        public bool Changed => Added.Count > 0 || Removed.Count > 0;
    }

    public static Result Migrate(IReadOnlyList<PolEntry> entries)
    {
        var notes = new List<string>();
        var removed = entries.Where(e => IsUnder(e.Key, LegacyKey)).ToList();
        if (removed.Count == 0)
        {
            return new Result(entries, [], [], notes);
        }

        var legacy = LegacyPolicyMapper.Map(Values(entries, LegacyKey));
        var newValues = Values(entries, NewKey);
        var current = PolicyMapper.Map(newValues,
            ListTexts(entries, NewKey + @"\" + PolicyMapper.LeftItemsSubkey),
            ListTexts(entries, NewKey + @"\" + PolicyMapper.RightItemsSubkey));

        var added = new List<PolEntry>();
        if (legacy is not null)
        {
            if (legacy.SetsMarking && current?.SetsMarking != true)
            {
                if (legacy.Custom is { } c)
                {
                    added.Add(PolEntry.Dword(NewKey, PolicyMapper.Names.CustomEnabled, 1));
                    added.Add(PolEntry.String(NewKey, PolicyMapper.Names.CustomText, c.Text));
                    added.Add(PolEntry.String(NewKey, PolicyMapper.Names.CustomBackground, c.Background));
                    added.Add(PolEntry.String(NewKey, PolicyMapper.Names.CustomForeground, c.Foreground));
                }
                else if (legacy.Classification is { } cls)
                {
                    added.Add(PolEntry.String(NewKey, PolicyMapper.Names.Classification, cls));
                    if (legacy.Caveats is { } caveats)
                    {
                        added.Add(PolEntry.String(NewKey, PolicyMapper.Names.Caveats, caveats));
                    }
                }
            }
            else if (legacy.SetsMarking)
            {
                notes.Add("Legacy marking not copied: NetBanner 3 policy already sets the marking.");
            }

            // INFOCON has no NetBanner 3 policy (CPCON replaced it); it becomes a left text item, which looks the same.
            if (legacy.LeftItems is { Count: > 0 } left)
            {
                if (current?.LeftItems is null)
                {
                    var listKey = NewKey + @"\" + PolicyMapper.LeftItemsSubkey;
                    added.Add(PolEntry.String(listKey, "**delvals.", " "));
                    for (int i = 0; i < left.Count; i++)
                    {
                        added.Add(PolEntry.String(listKey, (i + 1).ToString(CultureInfo.InvariantCulture),
                            BannerPresenter.DisplayText(left[i])));
                    }
                }
                else
                {
                    notes.Add("Legacy INFOCON not copied: NetBanner 3 policy already sets the left side.");
                }
            }

            if (legacy.RightItems is [{ Type: SideItemType.Fpcon } fpcon, ..])
            {
                if (current?.RightItems is null && !newValues.ContainsKey(PolicyMapper.Names.Fpcon))
                {
                    added.Add(PolEntry.String(NewKey, PolicyMapper.Names.Fpcon, fpcon.Value));
                }
                else
                {
                    notes.Add("Legacy FPCON not copied: NetBanner 3 policy already sets FPCON or the right side.");
                }
            }
        }

        // Drop gpedit "disabled" markers for anything we now set, so the new values aren't deleted on refresh.
        var addedNames = added.Where(a => !a.IsDirective)
            .Select(a => (a.Key, Del: "**del." + a.ValueName))
            .ToHashSet(KeyNameComparer.Instance);
        var addedLists = added.Where(a => a.ValueName == "**delvals.").Select(a => a.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var kept = entries
            .Where(e => !IsUnder(e.Key, LegacyKey))
            .Where(e => !addedNames.Contains((e.Key, e.ValueName)))
            .Where(e => !(addedLists.Contains(e.Key) && e.IsDirective))
            .ToList();

        return new Result([.. kept, .. added], added, removed, notes);
    }

    private static bool IsUnder(string key, string root)
    {
        var k = key.Trim('\\');
        return k.Equals(root, StringComparison.OrdinalIgnoreCase)
               || k.StartsWith(root + @"\", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, object?> Values(IEnumerable<PolEntry> entries, string key)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries.Where(e => e.Key.Trim('\\').Equals(key, StringComparison.OrdinalIgnoreCase) && !e.IsDirective && e.ValueName.Length > 0))
        {
            d[e.ValueName] = e.Value;
        }

        return d;
    }

    private static List<string>? ListTexts(IEnumerable<PolEntry> entries, string key)
    {
        var items = entries
            .Where(e => e.Key.Trim('\\').Equals(key, StringComparison.OrdinalIgnoreCase) && !e.IsDirective && e.ValueName.Length > 0)
            .Select(e => (Order: int.TryParse(e.ValueName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : int.MaxValue, Text: e.Value as string))
            .Where(x => x.Text is not null)
            .OrderBy(x => x.Order)
            .Select(x => x.Text!)
            .ToList();
        return items.Count > 0 ? items : null;
    }

    private sealed class KeyNameComparer : IEqualityComparer<(string Key, string Name)>
    {
        public static readonly KeyNameComparer Instance = new();

        public bool Equals((string Key, string Name) x, (string Key, string Name) y) =>
            string.Equals(x.Key.Trim('\\'), y.Key.Trim('\\'), StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Key, string Name) v) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(v.Key.Trim('\\')), StringComparer.OrdinalIgnoreCase.GetHashCode(v.Name));
    }
}
