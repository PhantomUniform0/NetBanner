using NetBanner.Core;

namespace NetBanner.Core.Tests;

public class SettingsValidatorTests
{
    private static (PartialSettings Result, List<string> Warnings) Normalize(PartialSettings p)
    {
        var w = new List<string>();
        return (SettingsValidator.Normalize(p, w), w);
    }

    [Theory]
    [InlineData("secret", MarkingCatalog.Secret)]
    [InlineData("TOP SECRET", MarkingCatalog.TopSecret)]
    [InlineData("top-secret", MarkingCatalog.TopSecret)]
    [InlineData("TOP SECRET//SCI", MarkingCatalog.TopSecretSci)]
    [InlineData("TS/SCI", MarkingCatalog.TopSecretSci)]
    [InlineData("TS", MarkingCatalog.TopSecret)]
    [InlineData("cui", MarkingCatalog.Cui)]
    public void Classification_Aliases_AreCanonicalized(string input, string expected)
    {
        var (r, w) = Normalize(new PartialSettings("T") { Classification = input });
        Assert.Equal(expected, r.Classification);
        Assert.Empty(w);
    }

    [Fact]
    public void UnknownClassification_BecomesNotConfigured_WithWarning()
    {
        var (r, w) = Normalize(new PartialSettings("T") { Classification = "SUPER SECRET" });
        Assert.Equal(MarkingCatalog.NotConfiguredId, r.Classification);
        Assert.Single(w);
    }

    [Fact]
    public void UnknownClassification_StillBlocksLowerSources()
    {
        // A typo in a GPO must not let a lower source's marking show through.
        var (policy, _) = Normalize(new PartialSettings("Policy") { Classification = "SECRT" });
        var local = new PartialSettings("Local") { Classification = MarkingCatalog.Unclassified };
        var merged = SettingsMerger.Merge([policy, local]);
        Assert.Equal(MarkingCatalog.NotConfiguredId, merged.Settings.Classification);
    }

    [Theory]
    [InlineData(5, 10)]
    [InlineData(200, 64)]
    public void Height_IsClamped(int input, int expected)
    {
        var (r, w) = Normalize(new PartialSettings("T") { Height = input });
        Assert.Equal(expected, r.Height);
        Assert.Single(w);
    }

    [Fact]
    public void FontSize_IsClamped()
    {
        var (r, _) = Normalize(new PartialSettings("T") { FontSize = 2 });
        Assert.Equal(SettingsValidator.MinFontSize, r.FontSize);
    }

    [Fact]
    public void CustomColors_AreNormalized_InvalidFallBackToBlackOnWhite()
    {
        var (r, w) = Normalize(new PartialSettings("T") { Custom = new CustomBanner("X", "f00", "not-a-color") });
        Assert.Equal(new CustomBanner("X", "#FF0000", "#000000"), r.Custom);
        Assert.Single(w);
    }

    [Fact]
    public void SideItems_InvalidAreDropped_ValidAreNormalized()
    {
        var (r, w) = Normalize(new PartialSettings("T")
        {
            LeftItems =
            [
                new SideItem(SideItemType.Cpcon, "4"),
                new SideItem(SideItemType.Cpcon, "9"),
                new SideItem(SideItemType.Fpcon, "bravo"),
                new SideItem(SideItemType.Fpcon, "ECHO"),
                new SideItem(SideItemType.Text, "  "),
                new SideItem(SideItemType.Text, "SITE", "#000", "bogus"),
            ],
        });

        Assert.Equal(
            [
                new SideItem(SideItemType.Cpcon, "4"),
                new SideItem(SideItemType.Fpcon, "BRAVO"),
                new SideItem(SideItemType.Text, "SITE", "#000000", null),
            ],
            r.LeftItems);
        Assert.Equal(4, w.Count);
    }

    [Fact]
    public void LongText_IsTruncated()
    {
        var (r, w) = Normalize(new PartialSettings("T") { Caveats = new string('X', 500) });
        Assert.Equal(SettingsValidator.MaxTextLength, r.Caveats!.Length);
        Assert.Single(w);
    }

    [Theory]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("ABCDEF", "#ABCDEF")]
    [InlineData(" #00ff00 ", "#00FF00")]
    public void ColorHex_Normalizes(string input, string expected)
    {
        Assert.True(ColorHex.TryNormalize(input, out var hex));
        Assert.Equal(expected, hex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("red")]
    [InlineData("#GGGGGG")]
    public void ColorHex_RejectsInvalid(string input) => Assert.False(ColorHex.TryNormalize(input, out _));
}
