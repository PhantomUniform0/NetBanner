using System.Globalization;

namespace NetBanner.Core;

/// <summary>
/// Cleans up a source's settings: canonical IDs and colors, clamped sizes, and invalid side items
/// dropped. Every problem is reported as a warning. Never throws.
/// </summary>
public static class SettingsValidator
{
    public const int MinHeight = 10, MaxHeight = 64;
    public const double MinFontSize = 6, MaxFontSize = 48;
    public const int MaxTextLength = 128;

    public static IReadOnlyList<string> FpconLevels { get; } = ["NORMAL", "ALPHA", "BRAVO", "CHARLIE", "DELTA"];

    public static PartialSettings Normalize(PartialSettings p, ICollection<string> warnings)
    {
        var src = p.SourceName;

        string? classification = p.Classification;
        if (classification is not null)
        {
            if (MarkingCatalog.TryGet(classification, out var m))
            {
                classification = m.Id;
            }
            else
            {
                warnings.Add($"{src}: unknown classification '{classification}'. Showing CLASSIFICATION NOT CONFIGURED.");
                classification = MarkingCatalog.NotConfiguredId;
            }
        }

        string? caveats = Truncate(RawValues.NullIfBlank(p.Caveats), "caveats", src, warnings);

        CustomBanner? custom = null;
        if (p.Custom is { } c)
        {
            custom = new CustomBanner(
                Truncate(c.Text?.Trim() ?? string.Empty, "custom text", src, warnings)!,
                Color(c.Background, "#FFFFFF", "custom background", src, warnings)!,
                Color(c.Foreground, "#000000", "custom foreground", src, warnings)!);
        }

        int? height = p.Height;
        if (height is { } h && (h < MinHeight || h > MaxHeight))
        {
            height = Math.Clamp(h, MinHeight, MaxHeight);
            warnings.Add($"{src}: height {h} is out of range; using {height}.");
        }

        double? fontSize = p.FontSize;
        if (fontSize is { } f && (double.IsNaN(f) || f < MinFontSize || f > MaxFontSize))
        {
            fontSize = double.IsNaN(f) ? null : Math.Clamp(f, MinFontSize, MaxFontSize);
            warnings.Add($"{src}: font size {f} is out of range; using {fontSize?.ToString(CultureInfo.InvariantCulture) ?? "default"}.");
        }

        return p with
        {
            Classification = classification,
            Caveats = caveats,
            Custom = custom,
            Height = height,
            FontSize = fontSize,
            LeftItems = Items(p.LeftItems, "left", src, warnings),
            RightItems = Items(p.RightItems, "right", src, warnings),
        };
    }

    private static IReadOnlyList<SideItem>? Items(IReadOnlyList<SideItem>? items, string side, string src, ICollection<string> warnings)
    {
        if (items is null)
        {
            return null;
        }

        var result = new List<SideItem>(items.Count);
        foreach (var item in items)
        {
            var value = item.Value?.Trim() ?? string.Empty;
            bool valid;
            switch (item.Type)
            {
                case SideItemType.Cpcon:
                case SideItemType.Infocon:
                    valid = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) && level is >= 1 and <= 5;
                    value = valid ? level.ToString(CultureInfo.InvariantCulture) : value;
                    break;
                case SideItemType.Fpcon:
                    value = value.ToUpperInvariant();
                    valid = FpconLevels.Contains(value);
                    break;
                default:
                    valid = value.Length > 0;
                    value = Truncate(value, "side item text", src, warnings)!;
                    break;
            }

            if (!valid)
            {
                warnings.Add($"{src}: dropped invalid {side} item {item.Type} '{item.Value}'.");
                continue;
            }

            result.Add(new SideItem(item.Type, value,
                Color(item.Background, null, $"{side} item background", src, warnings),
                Color(item.Foreground, null, $"{side} item foreground", src, warnings)));
        }

        return result;
    }

    private static string? Color(string? input, string? fallback, string what, string src, ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return fallback;
        }

        if (ColorHex.TryNormalize(input, out var hex))
        {
            return hex;
        }

        warnings.Add($"{src}: {what} '{input}' is not a #RRGGBB color; using {fallback ?? "the banner color"}.");
        return fallback;
    }

    private static string? Truncate(string? s, string what, string src, ICollection<string> warnings)
    {
        if (s is not null && s.Length > MaxTextLength)
        {
            warnings.Add($"{src}: {what} is longer than {MaxTextLength} characters; truncated.");
            return s[..MaxTextLength];
        }

        return s;
    }
}
