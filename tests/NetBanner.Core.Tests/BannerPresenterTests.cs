using NetBanner.Core;

namespace NetBanner.Core.Tests;

public class BannerPresenterTests
{
    [Fact]
    public void Default_ShowsNeutralNotConfiguredBanner()
    {
        var v = BannerPresenter.Present(BannerSettings.Default);
        Assert.Equal("CLASSIFICATION NOT CONFIGURED", v.CenterText);
        Assert.Equal("#FFFFFF", v.Background);
        Assert.Equal("#000000", v.Foreground);
    }

    [Fact]
    public void Classification_UsesCatalogColors()
    {
        var v = BannerPresenter.Present(new BannerSettings { Classification = MarkingCatalog.TopSecret });
        Assert.Equal("TOP SECRET", v.CenterText);
        Assert.Equal("#FF8C00", v.Background);
        Assert.Equal("#000000", v.Foreground);
    }

    [Theory]
    [InlineData("NOFORN", "SECRET//NOFORN")]
    [InlineData("//NOFORN", "SECRET//NOFORN")]
    [InlineData(@"\\ PHANTOM'S LAB", "SECRET//PHANTOM'S LAB")]
    [InlineData("- REL TO USA", "SECRET//REL TO USA")]
    [InlineData("  ", "SECRET")]
    [InlineData(null, "SECRET")]
    public void Caveats_AreJoinedWithDoubleSlash(string? caveats, string expected)
    {
        var v = BannerPresenter.Present(new BannerSettings { Classification = MarkingCatalog.Secret, Caveats = caveats });
        Assert.Equal(expected, v.CenterText);
    }

    [Fact]
    public void Caveats_NotAppended_WhenNotConfigured()
    {
        var v = BannerPresenter.Present(new BannerSettings { Caveats = "NOFORN" });
        Assert.Equal("CLASSIFICATION NOT CONFIGURED", v.CenterText);
    }

    [Fact]
    public void UnknownClassification_FallsBackToNotConfigured()
    {
        var v = BannerPresenter.Present(new BannerSettings { Classification = "BOGUS" });
        Assert.Equal("CLASSIFICATION NOT CONFIGURED", v.CenterText);
    }

    [Fact]
    public void Custom_OverridesEverything()
    {
        var v = BannerPresenter.Present(new BannerSettings
        {
            Classification = MarkingCatalog.Secret,
            Custom = new CustomBanner("EXERCISE EXERCISE", "#8B4513", "#FFFFFF"),
        });
        Assert.Equal("EXERCISE EXERCISE", v.CenterText);
        Assert.Equal("#8B4513", v.Background);
    }

    [Theory]
    [InlineData(SideItemType.Cpcon, "4", "CPCON 4")]
    [InlineData(SideItemType.Fpcon, "bravo", "FPCON BRAVO")]
    [InlineData(SideItemType.Infocon, "3", "INFOCON 3")]
    [InlineData(SideItemType.Text, "SITE: HQ", "SITE: HQ")]
    public void SideItems_DisplayText(SideItemType type, string value, string expected) =>
        Assert.Equal(expected, BannerPresenter.DisplayText(new SideItem(type, value)));
}
