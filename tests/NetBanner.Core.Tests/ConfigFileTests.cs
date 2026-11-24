using NetBanner.Core;

namespace NetBanner.Core.Tests;

public class ConfigFileTests
{
    private static (PartialSettings? Result, List<string> Warnings) Parse(string json)
    {
        var w = new List<string>();
        return (ConfigFile.Parse(json, w), w);
    }

    [Fact]
    public void FullDocument_Parses()
    {
        var (s, w) = Parse("""
            {
              // comments are allowed
              "classification": "SECRET",
              "caveats": "NOFORN",
              "height": 16,
              "fontSize": 13.5,
              "leftItems": [ { "type": "cpcon", "value": 4 }, { "type": "Text", "value": "SITE: {hostname}", "background": "#000000" } ],
              "rightItems": [ { "type": "FPCON", "value": "alpha" } ],
              "fullScreenBehavior": "yield",
            }
            """);

        Assert.Empty(w);
        Assert.Equal(ConfigFile.SourceName, s!.SourceName);
        Assert.Equal("SECRET", s.Classification);
        Assert.Equal("NOFORN", s.Caveats);
        Assert.Equal(16, s.Height);
        Assert.Equal(13.5, s.FontSize);
        Assert.Equal(
            [new SideItem(SideItemType.Cpcon, "4"), new SideItem(SideItemType.Text, "SITE: {hostname}", "#000000")],
            s.LeftItems);
        Assert.Equal([new SideItem(SideItemType.Fpcon, "alpha")], s.RightItems);
        Assert.Equal(FullScreenBehavior.Yield, s.FullScreenBehavior);
    }

    [Fact]
    public void Custom_OverridesClassification()
    {
        var (s, _) = Parse("""{ "classification": "SECRET", "custom": { "text": "TEST" } }""");
        Assert.Null(s!.Classification);
        Assert.Equal(new CustomBanner("TEST", "#FFFFFF", "#000000"), s.Custom);
    }

    [Fact]
    public void InvalidJson_ReturnsNull_WithWarning()
    {
        var (s, w) = Parse("{ not json");
        Assert.Null(s);
        Assert.Single(w);
    }

    [Fact]
    public void EmptyFile_ReturnsNull()
    {
        var (s, w) = Parse("   ");
        Assert.Null(s);
        Assert.Empty(w);
    }

    [Fact]
    public void UnknownItemType_AndFullScreen_AreWarned()
    {
        var (s, w) = Parse("""{ "leftItems": [ { "type": "Weather", "value": "x" } ], "fullScreenBehavior": "Sometimes" }""");
        Assert.Empty(s!.LeftItems!);
        Assert.Null(s.FullScreenBehavior);
        Assert.Equal(2, w.Count);
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var original = new PartialSettings(ConfigFile.SourceName)
        {
            Classification = "CUI",
            Height = 18,
            LeftItems = [new SideItem(SideItemType.Cpcon, "3")],
            RightItems = [new SideItem(SideItemType.Text, "HQ", "#112233", "#FFFFFF")],
            FullScreenBehavior = FullScreenBehavior.StayOnTop,
        };

        var (back, w) = Parse(ConfigFile.Serialize(original));

        Assert.Empty(w);
        Assert.Equal(original.Classification, back!.Classification);
        Assert.Equal(original.Height, back.Height);
        Assert.Equal(original.LeftItems, back.LeftItems);
        Assert.Equal(original.RightItems, back.RightItems);
        Assert.Equal(original.FullScreenBehavior, back.FullScreenBehavior);
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        var w = new List<string>();
        Assert.Null(ConfigFile.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), w));
        Assert.Empty(w);
    }

    [Fact]
    public async Task Watcher_RaisesChanged_WhenFileWritten()
    {
        var dir = Directory.CreateTempSubdirectory("nb-test-");
        try
        {
            var path = Path.Combine(dir.FullName, "config.json");
            using var watcher = new ConfigFileWatcher(path);
            var fired = new TaskCompletionSource();
            watcher.Changed += (_, _) => fired.TrySetResult();
            watcher.Start();

            await File.WriteAllTextAsync(path, """{ "classification": "CUI" }""", TestContext.Current.CancellationToken);

            var done = await Task.WhenAny(fired.Task, Task.Delay(5000, TestContext.Current.CancellationToken));
            Assert.Same(fired.Task, done);
        }
        finally
        {
            dir.Delete(true);
        }
    }
}
