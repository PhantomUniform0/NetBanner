using NetBanner.Core;

namespace NetBanner.Core.Tests;

public class SettingsMergerTests
{
    [Fact]
    public void NoSources_GivesDefaults_NotConfigured()
    {
        var r = SettingsMerger.Merge([]);
        Assert.Equal(MarkingCatalog.NotConfiguredId, r.Settings.Classification);
        Assert.Equal(SettingsMerger.DefaultSource, r.Provenance[nameof(BannerSettings.Classification)]);
        Assert.Equal(FullScreenBehavior.StayOnTop, r.Settings.FullScreenBehavior);
    }

    [Fact]
    public void NullSources_AreSkipped()
    {
        var r = SettingsMerger.Merge([null, new PartialSettings("B") { Classification = MarkingCatalog.Secret }]);
        Assert.Equal(MarkingCatalog.Secret, r.Settings.Classification);
        Assert.Equal("B", r.Provenance[nameof(BannerSettings.Classification)]);
    }

    [Fact]
    public void HigherSource_Wins_PerField()
    {
        var high = new PartialSettings("High") { Height = 30 };
        var low = new PartialSettings("Low") { Height = 20, FontSize = 14, Classification = MarkingCatalog.Cui };

        var r = SettingsMerger.Merge([high, low]);

        Assert.Equal(30, r.Settings.Height);
        Assert.Equal(14, r.Settings.FontSize);
        Assert.Equal(MarkingCatalog.Cui, r.Settings.Classification);
        Assert.Equal("High", r.Provenance[nameof(BannerSettings.Height)]);
        Assert.Equal("Low", r.Provenance[nameof(BannerSettings.FontSize)]);
    }

    [Fact]
    public void Marking_IsTakenAsOneGroup_LowerCustomCannotOverrideHigherClassification()
    {
        var policy = new PartialSettings("Policy") { Classification = MarkingCatalog.Secret };
        var local = new PartialSettings("Local") { Custom = new CustomBanner("FAKE", "#000000", "#FFFFFF"), Caveats = "X" };

        var r = SettingsMerger.Merge([policy, local]);

        Assert.Equal(MarkingCatalog.Secret, r.Settings.Classification);
        Assert.Null(r.Settings.Custom);
        Assert.Null(r.Settings.Caveats);
        Assert.Equal("Policy", r.Provenance[nameof(BannerSettings.Custom)]);
    }

    [Fact]
    public void SideItems_FallThrough_WhenHigherSourceDoesNotSetThem()
    {
        var policy = new PartialSettings("Policy") { Classification = MarkingCatalog.Secret };
        var legacy = new PartialSettings("Legacy") { RightItems = [new SideItem(SideItemType.Fpcon, "BRAVO")] };

        var r = SettingsMerger.Merge([policy, legacy]);

        Assert.Single(r.Settings.RightItems);
        Assert.Empty(r.Settings.LeftItems);
        Assert.Equal("Legacy", r.Provenance[nameof(BannerSettings.RightItems)]);
    }
}
