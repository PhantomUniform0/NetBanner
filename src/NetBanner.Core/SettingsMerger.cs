namespace NetBanner.Core;

public sealed record MergeResult(BannerSettings Settings, IReadOnlyDictionary<string, string> Provenance);

/// <summary>
/// Combines settings sources. Sources are passed highest precedence first.
/// <para>
/// The center marking (classification, caveats, custom banner) is one group: it all comes from the
/// highest source that sets any part of it. This keeps a lower source's custom banner from
/// overriding a GPO classification. Every other field merges independently.
/// </para>
/// </summary>
public static class SettingsMerger
{
    public const string DefaultSource = "Default";

    public static MergeResult Merge(IEnumerable<PartialSettings?> highestFirst)
    {
        var sources = highestFirst.OfType<PartialSettings>().ToList();
        var provenance = new Dictionary<string, string>(StringComparer.Ordinal);
        var d = BannerSettings.Default;

        var markingSource = sources.FirstOrDefault(s => s.SetsMarking);
        var markingName = markingSource?.SourceName ?? DefaultSource;
        provenance[nameof(BannerSettings.Classification)] = markingName;
        provenance[nameof(BannerSettings.Caveats)] = markingName;
        provenance[nameof(BannerSettings.Custom)] = markingName;

        var settings = new BannerSettings
        {
            Classification = markingSource?.Classification ?? d.Classification,
            Caveats = markingSource?.Caveats,
            Custom = markingSource?.Custom,
            Height = Pick(sources, s => s.Height, d.Height, nameof(BannerSettings.Height), provenance),
            FontSize = Pick(sources, s => s.FontSize, d.FontSize, nameof(BannerSettings.FontSize), provenance),
            LeftItems = PickRef(sources, s => s.LeftItems, d.LeftItems, nameof(BannerSettings.LeftItems), provenance),
            RightItems = PickRef(sources, s => s.RightItems, d.RightItems, nameof(BannerSettings.RightItems), provenance),
            FullScreenBehavior = Pick(sources, s => s.FullScreenBehavior, d.FullScreenBehavior,
                nameof(BannerSettings.FullScreenBehavior), provenance),
        };

        return new MergeResult(settings, provenance);
    }

    /// <summary>
    /// Combines sources into one partial result without filling in defaults, using the same rules as
    /// <see cref="Merge"/> (marking as a unit, other fields independently). Used by the NetBanner server to
    /// resolve machine > group > default, so clients can still layer the result between policy and local config.
    /// </summary>
    public static PartialSettings Combine(string sourceName, IEnumerable<PartialSettings?> highestFirst)
    {
        var sources = highestFirst.OfType<PartialSettings>().ToList();
        var marking = sources.FirstOrDefault(s => s.SetsMarking);
        return new PartialSettings(sourceName)
        {
            Classification = marking?.Classification,
            Caveats = marking?.Caveats,
            Custom = marking?.Custom,
            Height = sources.Select(s => s.Height).FirstOrDefault(v => v is not null),
            FontSize = sources.Select(s => s.FontSize).FirstOrDefault(v => v is not null),
            LeftItems = sources.Select(s => s.LeftItems).FirstOrDefault(v => v is not null),
            RightItems = sources.Select(s => s.RightItems).FirstOrDefault(v => v is not null),
            FullScreenBehavior = sources.Select(s => s.FullScreenBehavior).FirstOrDefault(v => v is not null),
        };
    }

    private static T Pick<T>(List<PartialSettings> sources, Func<PartialSettings, T?> get, T fallback,
        string field, Dictionary<string, string> provenance) where T : struct
    {
        foreach (var s in sources)
        {
            if (get(s) is { } value)
            {
                provenance[field] = s.SourceName;
                return value;
            }
        }

        provenance[field] = DefaultSource;
        return fallback;
    }

    private static T PickRef<T>(List<PartialSettings> sources, Func<PartialSettings, T?> get, T fallback,
        string field, Dictionary<string, string> provenance) where T : class
    {
        foreach (var s in sources)
        {
            if (get(s) is { } value)
            {
                provenance[field] = s.SourceName;
                return value;
            }
        }

        provenance[field] = DefaultSource;
        return fallback;
    }
}
