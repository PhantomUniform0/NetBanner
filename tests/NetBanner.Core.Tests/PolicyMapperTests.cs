using NetBanner.Core;
using N = NetBanner.Core.PolicyMapper.Names;

namespace NetBanner.Core.Tests;

public class PolicyMapperTests
{
    private static PartialSettings? Map(IReadOnlyList<string>? left = null, IReadOnlyList<string>? right = null,
        params (string Name, object? Value)[] values) =>
        PolicyMapper.Map(values.ToDictionary(v => v.Name, v => v.Value), left, right);

    [Fact]
    public void EmptyKey_ReturnsNull() => Assert.Null(Map());

    [Fact]
    public void Classification_AndCaveats()
    {
        var s = Map(values: [(N.Classification, "SECRET"), (N.Caveats, "NOFORN")]);
        Assert.Equal("SECRET", s!.Classification);
        Assert.Equal("NOFORN", s.Caveats);
        Assert.Equal(PolicyMapper.SourceName, s.SourceName);
    }

    [Fact]
    public void BlankCaveats_AreNull()
    {
        var s = Map(values: [(N.Classification, "CUI"), (N.Caveats, "  ")]);
        Assert.Null(s!.Caveats);
    }

    [Fact]
    public void CustomEnabled_OverridesClassification()
    {
        var s = Map(values:
        [
            (N.Classification, "SECRET"), (N.CustomEnabled, 1), (N.CustomText, " EXERCISE "),
            (N.CustomBackground, "#123456"), (N.CustomForeground, "#ABCDEF"),
        ]);
        Assert.Null(s!.Classification);
        Assert.Equal(new CustomBanner("EXERCISE", "#123456", "#ABCDEF"), s.Custom);
    }

    [Fact]
    public void Custom_WithoutColors_DefaultsToBlackOnWhite()
    {
        var s = Map(values: [(N.CustomEnabled, 1), (N.CustomText, "X")]);
        Assert.Equal(new CustomBanner("X", "#FFFFFF", "#000000"), s!.Custom);
    }

    [Fact]
    public void Cpcon_DefaultsLeft_Fpcon_DefaultsRight()
    {
        var s = Map(values: [(N.Cpcon, 4), (N.Fpcon, "BRAVO")]);
        Assert.Equal([new SideItem(SideItemType.Cpcon, "4")], s!.LeftItems);
        Assert.Equal([new SideItem(SideItemType.Fpcon, "BRAVO")], s.RightItems);
    }

    [Fact]
    public void Sides_CanBeSwapped()
    {
        var s = Map(values: [(N.Cpcon, 2), (N.CpconSide, "Right"), (N.Fpcon, "DELTA"), (N.FpconSide, "left")]);
        Assert.Equal(SideItemType.Fpcon, Assert.Single(s!.LeftItems!).Type);
        Assert.Equal(SideItemType.Cpcon, Assert.Single(s.RightItems!).Type);
    }

    [Fact]
    public void BuiltIns_SitAtOuterEdges()
    {
        var s = Map(left: ["L1", "L2"], right: ["R1"], values: [(N.Cpcon, 3), (N.Fpcon, "ALPHA")]);
        Assert.Equal(["CPCON 3", "L1", "L2"], s!.LeftItems!.Select(i => BannerPresenter.DisplayText(i)));
        Assert.Equal(["R1", "FPCON ALPHA"], s.RightItems!.Select(i => BannerPresenter.DisplayText(i)));
    }

    [Fact]
    public void BlankListEntries_AreSkipped()
    {
        var s = Map(left: ["  ", "SITE: HQ"]);
        Assert.Equal("SITE: HQ", Assert.Single(s!.LeftItems!).Value);
    }

    [Fact]
    public void Appearance()
    {
        var s = Map(values: [(N.Height, 20), (N.FontSize, 15), (N.FullScreenBehavior, "Yield")]);
        Assert.Equal(20, s!.Height);
        Assert.Equal(15, s.FontSize);
        Assert.Equal(FullScreenBehavior.Yield, s.FullScreenBehavior);
        Assert.Null(s.Classification);
    }
}
