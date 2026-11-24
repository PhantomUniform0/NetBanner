using NetBanner.Core;

namespace NetBanner.Core.Tests;

public class LegacyPolicyMigrationTests
{
    private const string L = LegacyPolicyMigration.LegacyKey;
    private const string N = LegacyPolicyMigration.NewKey;

    private static string? Get(IEnumerable<PolEntry> entries, string key, string name) =>
        entries.FirstOrDefault(e => e.Key == key && e.ValueName == name)?.Value.ToString();

    [Fact]
    public void RegistryPol_RoundTrips()
    {
        List<PolEntry> entries =
        [
            PolEntry.Dword(L, "Classification", 2),
            PolEntry.String(L, "Caveats", "NOFORN"),
            PolEntry.String(@"Software\Other", "**del.X", " "),
        ];

        var parsed = RegistryPol.Parse(RegistryPol.Serialize(entries));

        Assert.Equal(3, parsed.Count);
        Assert.Equal(2, parsed[0].Value);
        Assert.Equal("NOFORN", parsed[1].Value);
        Assert.True(parsed[2].IsDirective);
        Assert.Equal(entries.Select(e => (e.Key, e.ValueName, e.Type)), parsed.Select(e => (e.Key, e.ValueName, e.Type)));
    }

    [Fact]
    public void RegistryPol_EmptyFile_HasNoEntries() => Assert.Empty(RegistryPol.Parse([]));

    [Fact]
    public void RegistryPol_BadHeader_Throws() =>
        Assert.Throws<InvalidDataException>(() => RegistryPol.Parse("NOPE0000"u8));

    [Fact]
    public void RealLocalRegistryPol_RoundTripsByteForByte()
    {
        var path = Path.Combine(Environment.SystemDirectory, "GroupPolicy", "Machine", "Registry.pol");
        if (!OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return;
        }

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(bytes, RegistryPol.Serialize(RegistryPol.Parse(bytes)));
    }

    [Fact]
    public void NoLegacy_NoChange()
    {
        List<PolEntry> entries = [PolEntry.String(N, "Classification", "SECRET")];
        var r = LegacyPolicyMigration.Migrate(entries);
        Assert.False(r.Changed);
        Assert.Same(entries, r.Entries);
    }

    [Fact]
    public void Classification_Caveats_InfoCon_FpCon_AreMigrated_AndLegacyRemoved()
    {
        List<PolEntry> entries =
        [
            PolEntry.Dword(L, "Classification", 4),
            PolEntry.Dword(L, "CaveatsEnabled", 1),
            PolEntry.String(L, "Caveats", "NOFORN"),
            PolEntry.Dword(L, "InfoCon", 3),
            PolEntry.Dword(L, "FpCon", 2),
            PolEntry.String(@"Software\Policies\Other", "Keep", "me"),
        ];

        var r = LegacyPolicyMigration.Migrate(entries);

        Assert.DoesNotContain(r.Entries, e => e.Key.StartsWith(L, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("me", Get(r.Entries, @"Software\Policies\Other", "Keep"));
        Assert.Equal(MarkingCatalog.TopSecretSci, Get(r.Entries, N, "Classification"));
        Assert.Equal("NOFORN", Get(r.Entries, N, "Caveats"));
        Assert.Equal("INFOCON 3", Get(r.Entries, N + @"\LeftItems", "1"));
        Assert.Equal("BRAVO", Get(r.Entries, N, "Fpcon"));
        Assert.Equal(5, r.Removed.Count);
    }

    [Fact]
    public void Migration_ProducesTheSameBanner()
    {
        List<PolEntry> entries =
        [
            PolEntry.Dword(L, "Classification", 2),
            PolEntry.Dword(L, "InfoCon", 4),
            PolEntry.Dword(L, "FpCon", 1),
        ];
        var before = LegacyPolicyMapper.Map(entries.ToDictionary(e => e.ValueName, e => (object?)e.Value))!;

        var r = LegacyPolicyMigration.Migrate(entries);
        var after = PolicyMapper.Map(
            r.Entries.Where(e => e.Key == N).ToDictionary(e => e.ValueName, e => (object?)e.Value),
            r.Entries.Where(e => e.Key == N + @"\LeftItems" && !e.IsDirective).Select(e => (string)e.Value).ToList())!;

        Assert.Equal(before.Classification, after.Classification);
        Assert.Equal(before.LeftItems!.Select(i => BannerPresenter.DisplayText(i)), after.LeftItems!.Select(i => BannerPresenter.DisplayText(i)));
        Assert.Equal(before.RightItems!.Select(i => BannerPresenter.DisplayText(i)), after.RightItems!.Select(i => BannerPresenter.DisplayText(i)));
    }

    [Fact]
    public void CustomBanner_IsMigrated()
    {
        List<PolEntry> entries =
        [
            PolEntry.Dword(L, "Classification", 1),
            PolEntry.Dword(L, "CustomSettings", 1),
            PolEntry.String(L, "CustomDisplayText", "PHANTOMS LAB"),
            PolEntry.Dword(L, "CustomBackgroundColor", 8),
            PolEntry.Dword(L, "CustomForeColor", 2),
        ];

        var r = LegacyPolicyMigration.Migrate(entries);

        Assert.Equal("1", Get(r.Entries, N, "CustomEnabled"));
        Assert.Equal("PHANTOMS LAB", Get(r.Entries, N, "CustomText"));
        Assert.Equal("#800080", Get(r.Entries, N, "CustomBackground"));
        Assert.Equal("#FFFFFF", Get(r.Entries, N, "CustomForeground"));
        Assert.Null(Get(r.Entries, N, "Classification"));
    }

    [Fact]
    public void ExistingNetBanner3Settings_AreNotOverwritten()
    {
        List<PolEntry> entries =
        [
            PolEntry.String(N, "Classification", "SECRET"),
            PolEntry.Dword(N, "Cpcon", 4),
            PolEntry.Dword(L, "Classification", 1),
            PolEntry.Dword(L, "InfoCon", 3),
        ];

        var r = LegacyPolicyMigration.Migrate(entries);

        Assert.Equal("SECRET", Get(r.Entries, N, "Classification"));
        Assert.Null(Get(r.Entries, N + @"\LeftItems", "1"));
        Assert.Empty(r.Added);
        Assert.Equal(2, r.Removed.Count);
        Assert.Equal(2, r.Notes.Count);
    }

    [Fact]
    public void DisabledMarkerForMigratedValue_IsDropped()
    {
        List<PolEntry> entries =
        [
            PolEntry.String(N, "**del.Classification", " "),
            PolEntry.Dword(L, "Classification", 2),
        ];

        var r = LegacyPolicyMigration.Migrate(entries);

        Assert.DoesNotContain(r.Entries, e => e.ValueName == "**del.Classification");
        Assert.Equal(MarkingCatalog.Secret, Get(r.Entries, N, "Classification"));
    }

    [Fact]
    public void DisabledLegacyPolicies_AreJustRemoved()
    {
        List<PolEntry> entries = [PolEntry.String(L, "**del.Classification", " ")];
        var r = LegacyPolicyMigration.Migrate(entries);
        Assert.True(r.Changed);
        Assert.Empty(r.Entries);
        Assert.Empty(r.Added);
    }
}
