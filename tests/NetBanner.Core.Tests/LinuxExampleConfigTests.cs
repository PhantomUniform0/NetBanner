using NetBanner.Core;

namespace NetBanner.Core.Tests;

/// <summary>The example config shipped in the Linux packages must parse cleanly.</summary>
public class LinuxExampleConfigTests
{
    [Fact]
    public void ExampleConfig_ParsesWithoutWarnings()
    {
        var warnings = new List<string>();
        var path = Path.Combine(AppContext.BaseDirectory, "linux", "config.example.json");

        var raw = ConfigFile.Load(path, warnings);
        Assert.NotNull(raw);
        var settings = SettingsMerger.Merge([SettingsValidator.Normalize(raw, warnings)]).Settings;

        Assert.Empty(warnings);
        Assert.Equal(MarkingCatalog.Unclassified, settings.Classification);
        Assert.Equal("CPCON 5", BannerPresenter.DisplayText(Assert.Single(settings.LeftItems)));
        Assert.Equal("{hostname}", Assert.Single(settings.RightItems).Value);
    }
}
