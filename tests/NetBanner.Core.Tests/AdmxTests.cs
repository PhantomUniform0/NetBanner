using System.Text.RegularExpressions;
using System.Xml.Linq;
using NetBanner.Core;

namespace NetBanner.Core.Tests;

/// <summary>Keeps policy/NetBanner3.admx, its ADML, and the code that reads the registry in sync.</summary>
public partial class AdmxTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/GroupPolicy/2006/07/PolicyDefinitions";
    private static readonly string PolicyDir = Path.Combine(AppContext.BaseDirectory, "policy");

    private static XDocument Admx => XDocument.Load(Path.Combine(PolicyDir, "NetBanner3.admx"));
    private static XDocument Adml => XDocument.Load(Path.Combine(PolicyDir, "en-US", "NetBanner3.adml"));

    [GeneratedRegex(@"\$\(string\.([^)]+)\)")]
    private static partial Regex StringRef();

    [Fact]
    public void EveryStringReference_ExistsInAdml()
    {
        var strings = Adml.Descendants(Ns + "string").Select(s => (string)s.Attribute("id")!).ToHashSet();
        var refs = Admx.Descendants().Attributes()
            .SelectMany(a => StringRef().Matches(a.Value).Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();

        Assert.NotEmpty(refs);
        Assert.All(refs, r => Assert.Contains(r, strings));
    }

    [Fact]
    public void EveryPresentation_MatchesItsPolicyElements()
    {
        var presentations = Adml.Descendants(Ns + "presentation").ToDictionary(p => (string)p.Attribute("id")!);

        foreach (var policy in Admx.Descendants(Ns + "policy"))
        {
            var presRef = ((string?)policy.Attribute("presentation"))?.Replace("$(presentation.", "").TrimEnd(')');
            var elementIds = policy.Element(Ns + "elements")?.Elements().Select(e => (string)e.Attribute("id")!).ToHashSet() ?? [];

            if (presRef is null)
            {
                Assert.Empty(elementIds);
                continue;
            }

            Assert.True(presentations.TryGetValue(presRef, out var pres), $"Missing presentation {presRef}");
            var refIds = pres!.Elements().Select(e => (string?)e.Attribute("refId")).OfType<string>().ToHashSet();
            Assert.Equal(elementIds.Order(), refIds.Order());
        }
    }

    [Fact]
    public void RegistryValueNames_MatchPolicyMapper()
    {
        const string mainKey = @"Software\Policies\NetBanner";
        var admxNames = Admx.Descendants(Ns + "policy")
            .Where(p => (string)p.Attribute("key")! == mainKey)
            .SelectMany(p => p.Descendants().Attributes("valueName").Concat(p.Attributes("valueName")))
            .Select(a => a.Value)
            .ToHashSet();

        Assert.Equal(PolicyMapper.Names.All.Order(), admxNames.Order());

        var listKeys = Admx.Descendants(Ns + "list").Select(l => (string)l.Attribute("key")!).Order().ToList();
        Assert.Equal(
            new[] { $@"{mainKey}\{PolicyMapper.LeftItemsSubkey}", $@"{mainKey}\{PolicyMapper.RightItemsSubkey}" }.Order(),
            listKeys);
    }

    [Fact]
    public void EnumValues_AreAcceptedByTheValidator()
    {
        string[] EnumStrings(string id) => Admx.Descendants(Ns + "enum")
            .Single(e => (string)e.Attribute("id")! == id)
            .Descendants(Ns + "string").Select(s => s.Value).ToArray();

        Assert.All(EnumStrings("Classification"), c => Assert.True(MarkingCatalog.TryGet(c, out var m) && m.Id == c, c));
        Assert.Equal(MarkingCatalog.All.Select(m => m.Id).Order(), EnumStrings("Classification").Order());
        Assert.Equal(SettingsValidator.FpconLevels, EnumStrings("Fpcon"));
        Assert.All(EnumStrings("FullScreenBehavior"), f => Assert.True(Enum.TryParse<FullScreenBehavior>(f, out _), f));
    }

    [Fact]
    public void DefaultsInAdml_MatchBuiltInDefaults()
    {
        var decimals = Adml.Descendants(Ns + "decimalTextBox").ToDictionary(d => (string)d.Attribute("refId")!, d => (int)d.Attribute("defaultValue")!);
        Assert.Equal(BannerSettings.Default.Height, decimals["Height"]);
        Assert.Equal(BannerSettings.Default.FontSize, decimals["FontSize"]);
    }
}
