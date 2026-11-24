namespace NetBanner.Core;

public sealed record Marking(string Id, string Text, string Background, string Foreground);

/// <summary>Built-in classification markings and their default banner colors.</summary>
public static class MarkingCatalog
{
    public const string NotConfiguredId = "NOT_CONFIGURED";
    public const string Unclassified = "UNCLASSIFIED";
    public const string Cui = "CUI";
    public const string Confidential = "CONFIDENTIAL";
    public const string Secret = "SECRET";
    public const string TopSecret = "TOP_SECRET";
    public const string TopSecretSci = "TOP_SECRET_SCI";

    /// <summary>
    /// Shown when nothing sets a classification. Deliberately neutral, so an unconfigured
    /// machine never displays a wrong marking such as UNCLASSIFIED on a classified system.
    /// </summary>
    public static Marking NotConfigured { get; } =
        new(NotConfiguredId, "CLASSIFICATION NOT CONFIGURED", "#FFFFFF", "#000000");

    public static IReadOnlyList<Marking> All { get; } =
    [
        new(Unclassified, "UNCLASSIFIED", "#007A33", "#000000"),
        new(Cui, "CUI", "#502B85", "#000000"),
        new(Confidential, "CONFIDENTIAL", "#0033A0", "#000000"),
        new(Secret, "SECRET", "#C8102E", "#000000"),
        new(TopSecret, "TOP SECRET", "#FF8C00", "#000000"),
        new(TopSecretSci, "TOP SECRET//SCI", "#FCE83A", "#000000"),
    ];

    private static readonly Dictionary<string, Marking> ById = BuildLookup();

    private static Dictionary<string, Marking> BuildLookup()
    {
        var d = All.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        // Common shorthand admins type into config files.
        d["U"] = d[Unclassified];
        d["C"] = d[Confidential];
        d["S"] = d[Secret];
        d["TS"] = d[TopSecret];
        d["TS_SCI"] = d[TopSecretSci];
        d[NotConfiguredId] = NotConfigured;
        return d;
    }

    /// <summary>"TOP SECRET//SCI", "top-secret sci", "TS/SCI" all become e.g. TOP_SECRET_SCI or TS_SCI.</summary>
    private static string Canonical(string id)
    {
        var chars = id.Trim().ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var s = new string(chars);
        while (s.Contains("__", StringComparison.Ordinal))
        {
            s = s.Replace("__", "_", StringComparison.Ordinal);
        }

        return s.Trim('_');
    }

    /// <summary>Looks up a marking by ID or common spelling (case, spaces and slashes don't matter).</summary>
    public static bool TryGet(string? id, out Marking marking)
    {
        if (id is not null && ById.TryGetValue(Canonical(id), out var found))
        {
            marking = found;
            return true;
        }

        marking = NotConfigured;
        return false;
    }

    /// <summary>Returns the marking for <paramref name="id"/>, or <see cref="NotConfigured"/> if unknown.</summary>
    public static Marking Get(string? id) => TryGet(id, out var m) ? m : NotConfigured;
}
