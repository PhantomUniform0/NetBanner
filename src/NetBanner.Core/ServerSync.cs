using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NetBanner.Core;

/// <summary>
/// Client side of the NetBanner server. A privileged sync process (root / SYSTEM) fetches the signed config, verifies
/// it and writes it to an admin-only state folder; the banners (running as the user) read it back with
/// <see cref="LoadCached"/>, which verifies it again against the pinned key.
/// <list type="bullet">
/// <item><c>server-key</c>: the server's public key, pinned on first contact (trust on first use, like SSH).</item>
/// <item><c>server.json</c>: the last verified signed config. Kept while the server is unreachable.</item>
/// </list>
/// </summary>
public sealed class ServerSync : IDisposable
{
    public const string PinFile = "server-key";
    public const string CacheFile = "server.json";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private string? _etag;
    private string? _etagUrl;
    private string? _lastProblem;

    /// <summary>This machine's name as the server knows it.</summary>
    public static string Hostname
    {
        get
        {
            try
            {
                return ServerConfig.NormalizeHost(Dns.GetHostName());
            }
            catch
            {
                return ServerConfig.NormalizeHost(Environment.MachineName);
            }
        }
    }

    /// <summary>The verified server settings from the cache, or null (no server, never synced, or not verifiable).</summary>
    public static PartialSettings? LoadCached(string stateDir, ICollection<string> warnings)
    {
        var cachePath = Path.Combine(stateDir, CacheFile);
        if (!File.Exists(cachePath))
        {
            return null;
        }

        var pin = ReadPin(stateDir);
        if (pin is null)
        {
            warnings.Add($"{ServerConfig.SourceName}: cached config ignored (no pinned server key).");
            return null;
        }

        var cached = ReadCache(stateDir);
        if (cached is null)
        {
            warnings.Add($"{ServerConfig.SourceName}: cached config is unreadable.");
            return null;
        }

        var r = ServerConfig.Verify(cached, pin, Hostname, 0);
        if (r.Status != VerifyStatus.Ok)
        {
            warnings.Add($"{ServerConfig.SourceName}: cached config rejected: {r.Message}");
            return null;
        }

        var w = new List<string>();
        var parsed = ConfigFile.Parse(r.Payload!.Settings, w);
        foreach (var x in w)
        {
            warnings.Add(x.Replace(ConfigFile.SourceName, ServerConfig.SourceName));
        }

        return parsed is null ? null : parsed with { SourceName = ServerConfig.SourceName, ServerUrl = null };
    }

    /// <summary>The pinned key's fingerprint, or null.</summary>
    public static string? PinnedFingerprint(string stateDir) => ReadPin(stateDir) is { } k ? ServerConfig.Fingerprint(k) : null;

    /// <summary>Admin action after a deliberate server key change: the next sync pins the new key.</summary>
    public static void ForgetServerKey(string stateDir)
    {
        File.Delete(Path.Combine(stateDir, PinFile));
        File.Delete(Path.Combine(stateDir, CacheFile));
    }

    /// <summary>Syncs at start and then every <paramref name="interval"/> until cancelled.</summary>
    public async Task RunAsync(Func<string?> getUrl, string stateDir, TimeSpan interval, CancellationToken ct)
    {
        Log.Info($"Server sync started for '{Hostname}' (state: {stateDir}).");
        while (!ct.IsCancellationRequested)
        {
            var url = getUrl();
            if (url is null)
            {
                Problem("No server configured; nothing to sync.", warn: false);
            }
            else
            {
                await SyncOnceAsync(url, stateDir, ct);
            }

            try
            {
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<bool> SyncOnceAsync(string url, string stateDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(stateDir);
        if (_etagUrl != url)
        {
            _etag = null;
            _etagUrl = url;
        }

        var host = Hostname;
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{url.TrimEnd('/')}/api/config/{Uri.EscapeDataString(host)}");
            if (_etag is not null && File.Exists(Path.Combine(stateDir, CacheFile)))
            {
                request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(_etag));
            }

            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            Problem($"Cannot reach the server at {url} ({ex.Message}); keeping the last config.");
            return false;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                Ok();
                return true;
            }

            if (!response.IsSuccessStatusCode)
            {
                Problem($"Server at {url} returned {(int)response.StatusCode}; keeping the last config.");
                return false;
            }

            SignedConfig? signed;
            try
            {
                signed = JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(ct), ServerJsonContext.Default.SignedConfig);
            }
            catch (JsonException ex)
            {
                Problem($"Server at {url} sent an unreadable config ({ex.Message}).");
                return false;
            }

            if (signed is null)
            {
                Problem($"Server at {url} sent an empty config.");
                return false;
            }

            var pin = ReadPin(stateDir);
            long current = CurrentSerial(stateDir, pin);
            var r = ServerConfig.Verify(signed, pin, host, current);
            if (r.Status != VerifyStatus.Ok)
            {
                Problem(r.Message);
                return false;
            }

            if (pin is null)
            {
                WriteFile(Path.Combine(stateDir, PinFile), signed.PublicKey);
                Log.Info($"Pinned the server's key {ServerConfig.Fingerprint(signed.PublicKey)} (first contact with {url}).");
            }

            if (r.Payload!.Serial != current || !File.Exists(Path.Combine(stateDir, CacheFile)))
            {
                WriteFile(Path.Combine(stateDir, CacheFile), JsonSerializer.Serialize(signed, ServerJsonContext.Default.SignedConfig));
                Log.Info($"Applied server config {r.Payload.Serial} from {url}.");
            }

            _etag = response.Headers.ETag?.Tag;
            Ok();
            return true;
        }
    }

    private static long CurrentSerial(string stateDir, string? pin)
    {
        if (pin is null || ReadCache(stateDir) is not { } cached)
        {
            return 0;
        }

        var r = ServerConfig.Verify(cached, pin, Hostname, 0);
        return r.Status == VerifyStatus.Ok ? r.Payload!.Serial : 0;
    }

    private static string? ReadPin(string stateDir)
    {
        var path = Path.Combine(stateDir, PinFile);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    private static SignedConfig? ReadCache(string stateDir)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(stateDir, CacheFile)), ServerJsonContext.Default.SignedConfig);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Atomic write; world-readable so the banners (running as the user) can read it, writable only by us.</summary>
    private static void WriteFile(string path, string text)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }

        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Logs a problem once until it changes or clears, so a 60 s loop doesn't flood the log.</summary>
    private void Problem(string message, bool warn = true)
    {
        if (message == _lastProblem)
        {
            return;
        }

        _lastProblem = message;
        if (warn)
        {
            Log.Warn(message);
        }
        else
        {
            Log.Info(message);
        }
    }

    private void Ok()
    {
        if (_lastProblem is not null)
        {
            Log.Info("Server sync is working again.");
            _lastProblem = null;
        }
    }

    public void Dispose() => _http.Dispose();
}
