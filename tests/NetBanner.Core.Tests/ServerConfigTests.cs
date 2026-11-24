using System.Security.Cryptography;
using System.Text;
using NetBanner.Core;

namespace NetBanner.Core.Tests;

public class ServerConfigTests
{
    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static string PublicKey => Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo());

    private static SignedConfig Signed(string host = "debian13-testing", long serial = 5, string settings = """{"classification":"SECRET"}""") =>
        ServerConfig.Sign(Key, new ServerPayload(host, serial, DateTimeOffset.UtcNow, settings));

    [Fact]
    public void FirstContact_VerifiesAndReturnsPayload()
    {
        var r = ServerConfig.Verify(Signed(), pinnedPublicKey: null, "debian13-testing", minSerial: 0);
        Assert.Equal(VerifyStatus.Ok, r.Status);
        Assert.Equal(5, r.Payload!.Serial);
        Assert.Contains("SECRET", r.Payload.Settings);
    }

    [Fact]
    public void PinnedKey_Matches_Ok() =>
        Assert.Equal(VerifyStatus.Ok, ServerConfig.Verify(Signed(), PublicKey, "DEBIAN13-TESTING.lab.local", 5).Status);

    [Fact]
    public void DifferentKey_IsRejected()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var forged = ServerConfig.Sign(other, new ServerPayload("debian13-testing", 99, DateTimeOffset.UtcNow, """{"classification":"UNCLASSIFIED"}"""));
        Assert.Equal(VerifyStatus.KeyMismatch, ServerConfig.Verify(forged, PublicKey, "debian13-testing", 0).Status);
    }

    [Fact]
    public void TamperedPayload_IsRejected()
    {
        var s = Signed();
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(s.Payload)).Replace("SECRET", "UNCLASSIFIED");
        var tampered = s with { Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)) };
        Assert.Equal(VerifyStatus.BadSignature, ServerConfig.Verify(tampered, PublicKey, "debian13-testing", 0).Status);
    }

    [Fact]
    public void ConfigForAnotherHost_IsRejected() =>
        Assert.Equal(VerifyStatus.WrongHost, ServerConfig.Verify(Signed(host: "other-box"), PublicKey, "debian13-testing", 0).Status);

    [Fact]
    public void OlderSerial_IsRejected() =>
        Assert.Equal(VerifyStatus.Rollback, ServerConfig.Verify(Signed(serial: 3), PublicKey, "debian13-testing", 4).Status);

    [Fact]
    public void Garbage_IsMalformed() =>
        Assert.Equal(VerifyStatus.Malformed, ServerConfig.Verify(new SignedConfig("not base64!", "x", PublicKey), PublicKey, "h", 0).Status);

    [Fact]
    public void Fingerprint_IsSshStyle() =>
        Assert.Matches("^SHA256:[A-Za-z0-9+/]{43}$", ServerConfig.Fingerprint(PublicKey));

    [Fact]
    public void Combine_TakesMarkingAsUnit_AndOtherFieldsIndependently()
    {
        var machine = new PartialSettings("machine") { Caveats = "NOFORN", Classification = MarkingCatalog.Secret };
        var group = new PartialSettings("group") { Classification = MarkingCatalog.Unclassified, Caveats = "X", Height = 20 };
        var def = new PartialSettings("default") { FontSize = 15, Height = 16 };

        var c = SettingsMerger.Combine(ServerConfig.SourceName, [machine, group, def]);

        Assert.Equal(MarkingCatalog.Secret, c.Classification);
        Assert.Equal("NOFORN", c.Caveats);
        Assert.Equal(20, c.Height);
        Assert.Equal(15, c.FontSize);
        Assert.Null(c.LeftItems);
    }

    [Fact]
    public void Combine_NothingSet_IsEmpty()
    {
        var c = SettingsMerger.Combine(ServerConfig.SourceName, [null, new PartialSettings("default")]);
        Assert.False(c.SetsMarking);
        Assert.Null(c.Height);
    }
}
