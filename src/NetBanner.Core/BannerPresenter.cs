namespace NetBanner.Core;

public sealed record SideItemView(string Text, string? Background, string? Foreground);

/// <summary>Everything the UI needs to draw one banner. Colors are <c>#RRGGBB</c>.</summary>
public sealed record BannerView(
    string CenterText,
    string Background,
    string Foreground,
    IReadOnlyList<SideItemView> Left,
    IReadOnlyList<SideItemView> Right);

/// <summary>Turns resolved settings into display text and colors. No UI framework dependency.</summary>
public static class BannerPresenter
{
    /// <param name="tokens">Resolves <c>{token}</c> placeholders in custom text (e.g. <see cref="Tokens.FromSystem"/>). Null = no expansion.</param>
    public static BannerView Present(BannerSettings s, Func<string, string?>? tokens = null)
    {
        string text, bg, fg;
        if (s.Custom is { } c)
        {
            text = Tokens.Expand(c.Text, tokens);
            bg = c.Background;
            fg = c.Foreground;
        }
        else
        {
            var marking = MarkingCatalog.Get(s.Classification);
            text = marking.Text;
            bg = marking.Background;
            fg = marking.Foreground;

            // Legacy GPOs often include their own separator ("// X", @"\\ X"); we add "//" ourselves.
            var caveats = s.Caveats?.Trim().TrimStart('/', '\\', ' ', '-', '|').Trim();
            if (!string.IsNullOrEmpty(caveats) && marking.Id != MarkingCatalog.NotConfiguredId)
            {
                text = $"{text}//{caveats}";
            }
        }

        return new BannerView(text, bg, fg,
            s.LeftItems.Select(i => ToView(i, tokens)).ToList(),
            s.RightItems.Select(i => ToView(i, tokens)).ToList());
    }

    public static string DisplayText(SideItem item, Func<string, string?>? tokens = null) => item.Type switch
    {
        SideItemType.Cpcon => $"CPCON {item.Value}",
        SideItemType.Fpcon => $"FPCON {item.Value.ToUpperInvariant()}",
        SideItemType.Infocon => $"INFOCON {item.Value}",
        _ => Tokens.Expand(item.Value, tokens),
    };

    private static SideItemView ToView(SideItem item, Func<string, string?>? tokens) =>
        new(DisplayText(item, tokens), item.Background, item.Foreground);
}
