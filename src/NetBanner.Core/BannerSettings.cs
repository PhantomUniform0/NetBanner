namespace NetBanner.Core;

/// <summary>What the banner does when a full-screen application is in front on its monitor.</summary>
public enum FullScreenBehavior
{
    /// <summary>Keep the banner topmost and visible (default).</summary>
    StayOnTop,

    /// <summary>Drop behind the full-screen application, and return when it ends.</summary>
    Yield,
}

public enum SideItemType
{
    Text,
    Cpcon,
    Fpcon,

    /// <summary>Legacy only. Shown when a legacy GPO still sets INFOCON.</summary>
    Infocon,
}

/// <summary>An indicator shown on the left or right side of the banner.</summary>
/// <param name="Background">Optional <c>#RRGGBB</c>. Null means the banner's own color.</param>
/// <param name="Foreground">Optional <c>#RRGGBB</c>. Null means the banner's own color.</param>
public sealed record SideItem(SideItemType Type, string Value, string? Background = null, string? Foreground = null);

/// <summary>A fully custom center banner. Overrides classification and caveats from the same source.</summary>
public sealed record CustomBanner(string Text, string Background, string Foreground);

/// <summary>The fully resolved settings the banner renders from.</summary>
public sealed record BannerSettings
{
    public string Classification { get; init; } = MarkingCatalog.NotConfiguredId;
    public string? Caveats { get; init; }
    public CustomBanner? Custom { get; init; }

    /// <summary>Banner height in device-independent pixels. Scaled per monitor.</summary>
    public int Height { get; init; } = 14;

    public double FontSize { get; init; } = 13;
    public IReadOnlyList<SideItem> LeftItems { get; init; } = [];
    public IReadOnlyList<SideItem> RightItems { get; init; } = [];
    public FullScreenBehavior FullScreenBehavior { get; init; } = FullScreenBehavior.StayOnTop;

    public static BannerSettings Default { get; } = new();
}

/// <summary>
/// Settings from a single source (policy key, legacy key, local config). Null means "not set by this source".
/// </summary>
public sealed record PartialSettings(string SourceName)
{
    public string? Classification { get; init; }
    public string? Caveats { get; init; }
    public CustomBanner? Custom { get; init; }
    public int? Height { get; init; }
    public double? FontSize { get; init; }
    public IReadOnlyList<SideItem>? LeftItems { get; init; }
    public IReadOnlyList<SideItem>? RightItems { get; init; }
    public FullScreenBehavior? FullScreenBehavior { get; init; }

    /// <summary>
    /// Local config only: the NetBanner server to sync from (e.g. <c>http://10.0.0.5:8080</c>). Not a banner setting;
    /// carried here so reading and saving config.json keeps it.
    /// </summary>
    public string? ServerUrl { get; init; }

    /// <summary>True when this source sets any part of the center marking.</summary>
    public bool SetsMarking => Classification is not null || Caveats is not null || Custom is not null;
}
