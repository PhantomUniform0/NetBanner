using NetBanner.Core;

namespace NetBanner.Core.Tests;

public class LegacyPolicyMapperTests
{
    private static PartialSettings? Map(params (string Name, object? Value)[] values) =>
        LegacyPolicyMapper.Map(values.ToDictionary(v => v.Name, v => v.Value));

    [Fact]
    public void EmptyKey_ReturnsNull() => Assert.Null(Map());

    [Theory]
    [InlineData(1, MarkingCatalog.Unclassified)]
    [InlineData(2, MarkingCatalog.Secret)]
    [InlineData(3, MarkingCatalog.TopSecret)]
    [InlineData(4, MarkingCatalog.TopSecretSci)]
    public void Classification_MapsLegacyValues(int value, string expected)
    {
        var s = Map(("Classification", value));
        Assert.Equal(expected, s!.Classification);
        Assert.Equal(LegacyPolicyMapper.SourceName, s.SourceName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void Classification_OutOfRange_IsIgnored(int value) => Assert.Null(Map(("Classification", value)));

    [Fact]
    public void Values_StoredAsStrings_AreAccepted()
    {
        var s = Map(("Classification", " 2 "), ("FpCon", "3"));
        Assert.Equal(MarkingCatalog.Secret, s!.Classification);
        Assert.Equal("CHARLIE", s.RightItems![0].Value);
    }

    [Fact]
    public void ValueNames_AreCaseInsensitive()
    {
        var s = Map(("classification", 2));
        Assert.Equal(MarkingCatalog.Secret, s!.Classification);
    }

    [Fact]
    public void Caveats_OnlyWhenEnabled()
    {
        Assert.Null(Map(("Classification", 2), ("Caveats", "NOFORN"))!.Caveats);
        Assert.Null(Map(("Classification", 2), ("CaveatsEnabled", 0), ("Caveats", "NOFORN"))!.Caveats);
        Assert.Equal("NOFORN", Map(("Classification", 2), ("CaveatsEnabled", 1), ("Caveats", " NOFORN "))!.Caveats);
    }

    [Fact]
    public void CustomSettings_OverrideClassificationAndCaveats()
    {
        var s = Map(
            ("Classification", 2), ("CaveatsEnabled", 1), ("Caveats", "NOFORN"),
            ("CustomSettings", 1), ("CustomDisplayText", "EXERCISE"),
            ("CustomBackgroundColor", 8), ("CustomForeColor", 2));

        Assert.Null(s!.Classification);
        Assert.Null(s.Caveats);
        Assert.Equal(new CustomBanner("EXERCISE", "#800080", "#FFFFFF"), s.Custom);
    }

    [Fact]
    public void CustomSettings_MissingColors_UseBlackOnWhiteText()
    {
        var s = Map(("CustomSettings", 1), ("CustomDisplayText", "TEST"));
        Assert.Equal(new CustomBanner("TEST", "#000000", "#FFFFFF"), s!.Custom);
    }

    [Fact]
    public void InfoCon_GoesLeft_FpCon_GoesRight()
    {
        var s = Map(("InfoCon", 3), ("FpCon", 1));
        Assert.Equal([new SideItem(SideItemType.Infocon, "3")], s!.LeftItems);
        Assert.Equal([new SideItem(SideItemType.Fpcon, "ALPHA")], s.RightItems);
        Assert.Null(s.Classification);
    }

    [Fact]
    public void InfoCon_OutOfRange_IsIgnored() => Assert.Null(Map(("InfoCon", 6)));
}
