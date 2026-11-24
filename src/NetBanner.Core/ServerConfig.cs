using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetBanner.Core;

/// <summary>What the server signs for one machine. <see cref="Settings"/> is a config.json document.</summary>
public sealed record ServerPayload(string Hostname, long Serial, DateTimeOffset Issued, string Settings);

/// <summary>
/// The server's response: the payload (base64 of its UTF-8 JSON), an ECDSA P-256 signature over those bytes, and the
/// server's public key (SubjectPublicKeyInfo, base64), which clients pin on first contact like an SSH host key.
/// </summary>
public sealed record SignedConfig(string Payload, string Signature, string PublicKey);

public enum VerifyStatus
{
    Ok,
    /// <summary>Signed by a different key than the one pinned on this machine.</summary>
    KeyMismatch,
    BadSignature,
    /// <summary>Signed for another machine.</summary>
    WrongHost,
    /// <summary>Older than a config this machine already accepted (replay / rollback).</summary>
    Rollback,
    Malformed,
}

public sealed record VerifyResult(VerifyStatus Status, ServerPayload? Payload, string Message);

/// <summary>Signing and verification of server configs. Shared by the server and the clients.</summary>
public static class ServerConfig
{
    public const string SourceName = "Server";

    /// <summary>Short hostname, lower case: how machines are named on the server and in signed payloads.</summary>
    public static string NormalizeHost(string host) => host.Trim().Split('.')[0].ToLowerInvariant();

    /// <summary>SSH-style fingerprint of a public key: "SHA256:" + unpadded base64 of the SHA-256 of the key.</summary>
    public static string Fingerprint(string publicKeyBase64) =>
        "SHA256:" + Convert.ToBase64String(SHA256.HashData(Convert.FromBase64String(publicKeyBase64))).TrimEnd('=');

    public static SignedConfig Sign(ECDsa key, ServerPayload payload)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, ServerJsonContext.Default.ServerPayload));
        var signature = key.SignData(bytes, HashAlgorithmName.SHA256);
        return new SignedConfig(
            Convert.ToBase64String(bytes),
            Convert.ToBase64String(signature),
            Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }

    /// <param name="pinnedPublicKey">The key pinned on this machine (base64), or null on first contact.</param>
    /// <param name="minSerial">The serial of the last config this machine accepted (0 if none).</param>
    public static VerifyResult Verify(SignedConfig signed, string? pinnedPublicKey, string hostname, long minSerial)
    {
        if (pinnedPublicKey is not null && !string.Equals(pinnedPublicKey, signed.PublicKey, StringComparison.Ordinal))
        {
            return new VerifyResult(VerifyStatus.KeyMismatch, null,
                $"Server key changed: pinned {Fingerprint(pinnedPublicKey)}, server sent {SafeFingerprint(signed.PublicKey)}. " +
                "Keeping the last config. An administrator must clear the pinned key on this machine to accept the new one.");
        }

        byte[] bytes, signature;
        try
        {
            bytes = Convert.FromBase64String(signed.Payload);
            signature = Convert.FromBase64String(signed.Signature);
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(signed.PublicKey), out _);
            if (!key.VerifyData(bytes, signature, HashAlgorithmName.SHA256))
            {
                return new VerifyResult(VerifyStatus.BadSignature, null, "Server config signature is invalid.");
            }
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return new VerifyResult(VerifyStatus.Malformed, null, $"Server config is malformed: {ex.Message}");
        }

        ServerPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize(bytes, ServerJsonContext.Default.ServerPayload);
        }
        catch (JsonException ex)
        {
            return new VerifyResult(VerifyStatus.Malformed, null, $"Server config payload is malformed: {ex.Message}");
        }

        if (payload is null)
        {
            return new VerifyResult(VerifyStatus.Malformed, null, "Server config payload is empty.");
        }

        if (NormalizeHost(payload.Hostname) != NormalizeHost(hostname))
        {
            return new VerifyResult(VerifyStatus.WrongHost, null,
                $"Server config is for '{payload.Hostname}', not this machine ('{hostname}').");
        }

        if (payload.Serial < minSerial)
        {
            return new VerifyResult(VerifyStatus.Rollback, null,
                $"Server config serial {payload.Serial} is older than the accepted {minSerial}; ignored.");
        }

        return new VerifyResult(VerifyStatus.Ok, payload, $"Server config {payload.Serial} verified.");
    }

    private static string SafeFingerprint(string key)
    {
        try
        {
            return Fingerprint(key);
        }
        catch (FormatException)
        {
            return "(invalid key)";
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ServerPayload))]
[JsonSerializable(typeof(SignedConfig))]
public sealed partial class ServerJsonContext : JsonSerializerContext;
