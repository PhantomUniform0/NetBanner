using NetBanner.Core;

namespace NetBanner.Core.Tests;

public class TokensTests
{
    private static string? Fake(string name) => name switch
    {
        "hostname" => "WS01",
        "ip" => "10.0.0.5",
        _ => null,
    };

    [Theory]
    [InlineData("{hostname}", "WS01")]
    [InlineData("SITE {HOSTNAME} / {ip}", "SITE WS01 / 10.0.0.5")]
    [InlineData("{unknown} stays", "{unknown} stays")]
    [InlineData("no tokens", "no tokens")]
    public void Expand(string input, string expected) => Assert.Equal(expected, Tokens.Expand(input, Fake));

    [Fact]
    public void NullResolver_LeavesTextAlone() => Assert.Equal("{hostname}", Tokens.Expand("{hostname}", null));

    [Fact]
    public void FromSystem_ResolvesAllSupportedTokens()
    {
        foreach (var t in Tokens.Supported)
        {
            Assert.False(string.IsNullOrEmpty(Tokens.FromSystem(t)), t);
        }
    }

    [Fact]
    public void Presenter_ExpandsTokens_InTextItemsAndCustom_NotInCpcon()
    {
        var v = BannerPresenter.Present(new BannerSettings
        {
            Custom = new CustomBanner("TEST {hostname}", "#FFFFFF", "#000000"),
            LeftItems = [new SideItem(SideItemType.Text, "{ip}"), new SideItem(SideItemType.Cpcon, "3")],
        }, Fake);

        Assert.Equal("TEST WS01", v.CenterText);
        Assert.Equal(["10.0.0.5", "CPCON 3"], v.Left.Select(i => i.Text));
    }
}
