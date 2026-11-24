using System.Globalization;
using static NetBanner.Core.RawValues;

namespace NetBanner.Core;

/// <summary>
/// Converts values from the NetBanner 3 policy key (<c>HKLM\SOFTWARE\Policies\NetBanner</c>, written by
/// <c>policy/NetBanner3.admx</c>) into <see cref="PartialSettings"/>. OS-independent for unit testing.
/// </summary>
public static class PolicyMapper
{
    public const string SourceName = "Policy";
    public const string LeftItemsSubkey = "LeftItems";
    public const string RightItemsSubkey = "RightItems";

    /// <summary>Registry value names. These must match the ADMX (a unit test checks this).</summary>
    public static class Names
    {
        public const string Classification = "Classification";
        public const string Caveats = "Caveats";
        public const string CustomEnabled = "CustomEnabled";
        public const string CustomText = "CustomText";
        public const string CustomBackground = "CustomBackground";
        public const string CustomForeground = "CustomForeground";
        public const string Cpcon = "Cpcon";
        public const string CpconSide = "CpconSide";
        public const string Fpcon = "Fpcon";
        public const string FpconSide = "FpconSide";
        public const string Height = "Height";
        public const string FontSize = "FontSize";
        public const string FullScreenBehavior = "FullScreenBehavior";

        public static IReadOnlyList<string> All { get; } =
        [
            Classification, Caveats, CustomEnabled, CustomText, CustomBackground, CustomForeground,
            Cpcon, CpconSide, Fpcon, FpconSide, Height, FontSize, FullScreenBehavior,
        ];
    }

    /// <param name="values">Values directly under the policy key.</param>
    /// <param name="leftTexts">Text items from the <c>LeftItems</c> subkey, in order (null if absent).</param>
    /// <param name="rightTexts">Text items from the <c>RightItems</c> subkey, in order (null if absent).</param>
    /// <returns>Null when nothing applicable is configured.</returns>
    public static PartialSettings? Map(IReadOnlyDictionary<string, object?> values,
        IReadOnlyList<string>? leftTexts = null, IReadOnlyList<string>? rightTexts = null)
    {
        var v = CaseInsensitive(values);

        CustomBanner? custom = null;
        if (GetInt(v, Names.CustomEnabled) is { } ce && ce != 0)
        {
            custom = new CustomBanner(
                GetString(v, Names.CustomText)?.Trim() ?? string.Empty,
                GetString(v, Names.CustomBackground) ?? "#FFFFFF",
                GetString(v, Names.CustomForeground) ?? "#000000");
        }

        // Built-in indicators sit at the outer edges: on the left they come first, on the right last.
        var leftBuiltIn = new List<SideItem>();
        var rightBuiltIn = new List<SideItem>();

        if (GetInt(v, Names.Cpcon) is { } cpcon)
        {
            var item = new SideItem(SideItemType.Cpcon, cpcon.ToString(CultureInfo.InvariantCulture));
            (IsRight(GetString(v, Names.CpconSide), defaultRight: false) ? rightBuiltIn : leftBuiltIn).Add(item);
        }

        if (NullIfBlank(GetString(v, Names.Fpcon)) is { } fpcon)
        {
            var item = new SideItem(SideItemType.Fpcon, fpcon);
            (IsRight(GetString(v, Names.FpconSide), defaultRight: true) ? rightBuiltIn : leftBuiltIn).Add(item);
        }

        var leftCustom = Texts(leftTexts);
        var rightCustom = Texts(rightTexts);

        var result = new PartialSettings(SourceName)
        {
            Classification = custom is null ? NullIfBlank(GetString(v, Names.Classification)) : null,
            Caveats = custom is null ? NullIfBlank(GetString(v, Names.Caveats)) : null,
            Custom = custom,
            LeftItems = leftBuiltIn.Count + leftCustom.Count > 0 ? [.. leftBuiltIn, .. leftCustom] : null,
            RightItems = rightBuiltIn.Count + rightCustom.Count > 0 ? [.. rightCustom, .. rightBuiltIn] : null,
            Height = GetInt(v, Names.Height),
            FontSize = GetInt(v, Names.FontSize),
            FullScreenBehavior = Enum.TryParse<FullScreenBehavior>(GetString(v, Names.FullScreenBehavior), true, out var fs) ? fs : null,
        };

        bool any = result.SetsMarking || result.LeftItems is not null || result.RightItems is not null
                   || result.Height is not null || result.FontSize is not null || result.FullScreenBehavior is not null;
        return any ? result : null;
    }

    private static bool IsRight(string? side, bool defaultRight) =>
        side?.Trim().ToUpperInvariant() switch
        {
            "RIGHT" => true,
            "LEFT" => false,
            _ => defaultRight,
        };

    private static List<SideItem> Texts(IReadOnlyList<string>? texts) =>
        texts?.Select(t => t.Trim()).Where(t => t.Length > 0).Select(t => new SideItem(SideItemType.Text, t)).ToList() ?? [];
}
