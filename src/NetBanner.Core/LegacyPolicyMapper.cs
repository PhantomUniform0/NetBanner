using System.Globalization;
using static NetBanner.Core.RawValues;

namespace NetBanner.Core;

/// <summary>
/// Converts values from the legacy NetBanner policy key
/// (<c>HKLM\SOFTWARE\Policies\Microsoft\NetBanner</c>) into <see cref="PartialSettings"/>.
/// OS-independent so it can be unit tested: the caller passes the raw registry values.
/// </summary>
public static class LegacyPolicyMapper
{
    public const string SourceName = "LegacyPolicy";

    private static readonly string?[] Classifications =
        [null, MarkingCatalog.Unclassified, MarkingCatalog.Secret, MarkingCatalog.TopSecret, MarkingCatalog.TopSecretSci];

    private static readonly string?[] FpCons = [null, "ALPHA", "BRAVO", "CHARLIE", "DELTA"];

    // System.Drawing named colors the legacy app used.
    private static readonly string?[] BackColors =
        [null, "#008000", "#0000FF", "#FF0000", "#FFFF00", "#FFFFFF", "#000000", "#8B4513", "#800080", "#FFA500"];

    private static readonly string?[] ForeColors = [null, "#000000", "#FFFFFF", "#FF0000"];

    /// <returns>Null when the key has no values that apply.</returns>
    public static PartialSettings? Map(IReadOnlyDictionary<string, object?> values)
    {
        var v = CaseInsensitive(values);

        string? classification = Lookup(Classifications, GetInt(v, "Classification"));

        string? caveats = null;
        if (GetInt(v, "CaveatsEnabled") is { } ce && ce != 0)
        {
            caveats = NullIfBlank(GetString(v, "Caveats"));
        }

        CustomBanner? custom = null;
        if (GetInt(v, "CustomSettings") is { } cs && cs != 0)
        {
            custom = new CustomBanner(
                GetString(v, "CustomDisplayText")?.Trim() ?? string.Empty,
                Lookup(BackColors, GetInt(v, "CustomBackgroundColor")) ?? "#000000",
                Lookup(ForeColors, GetInt(v, "CustomForeColor")) ?? "#FFFFFF");
        }

        IReadOnlyList<SideItem>? left = null;
        if (GetInt(v, "InfoCon") is >= 1 and <= 5 and var infocon)
        {
            left = [new SideItem(SideItemType.Infocon, infocon.ToString(CultureInfo.InvariantCulture))];
        }

        IReadOnlyList<SideItem>? right = null;
        if (Lookup(FpCons, GetInt(v, "FpCon")) is { } fpcon)
        {
            right = [new SideItem(SideItemType.Fpcon, fpcon)];
        }

        var result = new PartialSettings(SourceName)
        {
            // In the legacy app, custom mode ignores Classification and Caveats.
            Classification = custom is null ? classification : null,
            Caveats = custom is null ? caveats : null,
            Custom = custom,
            LeftItems = left,
            RightItems = right,
        };

        bool any = result.SetsMarking || left is not null || right is not null;
        return any ? result : null;
    }

    private static string? Lookup(string?[] table, int? index) =>
        index is { } i && i > 0 && i < table.Length ? table[i] : null;
}
