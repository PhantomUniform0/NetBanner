using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetBanner.Core;

namespace NetBanner.Server;

/// <summary>A machine on the server: its group and optional per-machine settings (a config.json document).</summary>
public sealed class MachineEntry
{
    public string? Group { get; set; }
    public JsonElement? Settings { get; set; }
}

/// <summary>
/// Everything the server knows, stored in one file (data.json). Settings at each level are config.json documents.
/// Resolution: machine > its group > default.
/// </summary>
public sealed class ServerData
{
    public long Serial { get; set; }
    public JsonElement? Default { get; set; }
    public Dictionary<string, JsonElement> Groups { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, MachineEntry> Machines { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Data, signing key and admin password, kept in the server's data folder.</summary>
public sealed class ServerStore : IDisposable
{
    private readonly object _lock = new();
    private readonly string _dataPath;
    private readonly string _keyPath;
    private readonly string _adminPath;
    private readonly FileSystemWatcher? _watcher;
    private ServerData _data = new();
    private string _dataHash = string.Empty;

    public ServerStore(string folder)
    {
        Folder = folder;
        Directory.CreateDirectory(folder);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        _dataPath = Path.Combine(folder, "data.json");
        _keyPath = Path.Combine(folder, "signing-key.pem");
        _adminPath = Path.Combine(folder, "admin.json");

        Key = LoadOrCreateKey();
        PublicKey = Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo());
        Load();

        // Admins may edit data.json directly; pick that up (and bump the serial so clients take it).
        _watcher = new FileSystemWatcher(folder, "data.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
        _watcher.Changed += (_, _) => ReloadAfterExternalEdit();
        _watcher.Created += (_, _) => ReloadAfterExternalEdit();
        _watcher.Renamed += (_, _) => ReloadAfterExternalEdit();
        _watcher.EnableRaisingEvents = true;
    }

    public string Folder { get; }

    public ECDsa Key { get; }

    public string PublicKey { get; }

    public string Fingerprint => ServerConfig.Fingerprint(PublicKey);

    public ServerData Snapshot()
    {
        lock (_lock)
        {
            return Clone(_data);
        }
    }

    /// <summary>Replaces all data (from the web UI). Returns the new serial.</summary>
    public long Save(ServerData data)
    {
        lock (_lock)
        {
            data.Serial = Math.Max(_data.Serial, data.Serial) + 1;
            Write(data);
            return data.Serial;
        }
    }

    // ------------------------------------------------------------------ resolution

    public static PartialSettings? ParseLayer(JsonElement? doc, string name, List<string> warnings)
    {
        if (doc is not { ValueKind: JsonValueKind.Object } element)
        {
            return null;
        }

        var w = new List<string>();
        var parsed = ConfigFile.Parse(element.GetRawText(), w);
        var normalized = parsed is null ? null : SettingsValidator.Normalize(parsed, w) with { SourceName = name };
        warnings.AddRange(w.Select(x => $"{name}: {x.Replace(ConfigFile.SourceName + ": ", string.Empty)}"));
        return normalized;
    }

    /// <summary>Layers for a machine, highest first: machine, its group, default.</summary>
    public static List<PartialSettings?> Layers(ServerData data, string host, List<string> warnings)
    {
        var layers = new List<PartialSettings?>();
        if (data.Machines.TryGetValue(ServerConfig.NormalizeHost(host), out var m))
        {
            layers.Add(ParseLayer(m.Settings, $"machine {host}", warnings));
            if (!string.IsNullOrWhiteSpace(m.Group))
            {
                if (data.Groups.TryGetValue(m.Group, out var g))
                {
                    layers.Add(ParseLayer(g, $"group {m.Group}", warnings));
                }
                else
                {
                    warnings.Add($"machine {host}: group '{m.Group}' does not exist.");
                }
            }
        }

        layers.Add(ParseLayer(data.Default, "default", warnings));
        return layers;
    }

    /// <summary>The signed config for one machine.</summary>
    public SignedConfig ConfigFor(string host)
    {
        var data = Snapshot();
        var warnings = new List<string>();
        var combined = SettingsMerger.Combine(ServerConfig.SourceName, Layers(data, host, warnings));
        var payload = new ServerPayload(ServerConfig.NormalizeHost(host), data.Serial, DateTimeOffset.UtcNow, ConfigFile.Serialize(combined));
        return ServerConfig.Sign(Key, payload);
    }

    public long Serial
    {
        get
        {
            lock (_lock)
            {
                return _data.Serial;
            }
        }
    }

    // ------------------------------------------------------------------ auto-add

    /// <summary>Most machines the server adds on its own; protects against junk requests filling the list.</summary>
    public const int MaxMachines = 5000;

    /// <summary>
    /// Adds a machine the first time it asks for its config (no group, so it gets the default). Only plausible
    /// hostnames are accepted, since the endpoint is unauthenticated.
    /// </summary>
    public void EnsureMachine(string host)
    {
        var name = ServerConfig.NormalizeHost(host);
        if (name.Length is 0 or > 63 || !name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-') || name[0] == '-')
        {
            return;
        }

        lock (_lock)
        {
            if (_data.Machines.ContainsKey(name) || _data.Machines.Count >= MaxMachines)
            {
                return;
            }

            var d = Clone(_data);
            d.Machines[name] = new MachineEntry();
            d.Serial = _data.Serial + 1;
            Write(d);
            Console.WriteLine($"New machine '{name}' added automatically (no group: it gets the default).");
        }
    }

    // ------------------------------------------------------------------ admin password

    public bool HasAdminPassword => File.Exists(_adminPath);

    public void SetAdminPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        const int iterations = 210_000;
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        var record = new AdminRecord(Convert.ToBase64String(salt), Convert.ToBase64String(hash), iterations);
        WriteSecret(_adminPath, JsonSerializer.Serialize(record, StoreJsonContext.Default.AdminRecord));
    }

    public bool CheckAdminPassword(string password)
    {
        if (!HasAdminPassword)
        {
            return false;
        }

        var record = JsonSerializer.Deserialize(File.ReadAllText(_adminPath), StoreJsonContext.Default.AdminRecord)!;
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(record.Salt), record.Iterations, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(record.Hash));
    }

    // ------------------------------------------------------------------ files

    private ECDsa LoadOrCreateKey()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (File.Exists(_keyPath))
        {
            key.ImportFromPem(File.ReadAllText(_keyPath));
            return key;
        }

        WriteSecret(_keyPath, key.ExportPkcs8PrivateKeyPem());
        Console.WriteLine($"Generated a new signing key: {ServerConfig.Fingerprint(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()))}");
        return key;
    }

    private void Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_dataPath))
            {
                _data = new ServerData { Serial = 1 };
                Write(_data);
                return;
            }

            var text = File.ReadAllText(_dataPath);
            _data = Deserialize(text);
            _dataHash = Hash(text);
        }
    }

    private void ReloadAfterExternalEdit()
    {
        Thread.Sleep(300); // editors write in steps
        lock (_lock)
        {
            string text;
            try
            {
                text = File.ReadAllText(_dataPath);
            }
            catch (IOException)
            {
                return;
            }

            if (Hash(text) == _dataHash)
            {
                return; // our own write
            }

            ServerData edited;
            try
            {
                edited = Deserialize(text);
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"data.json was edited but is not valid JSON ({ex.Message}); keeping the previous data.");
                return;
            }

            edited.Serial = Math.Max(edited.Serial, _data.Serial) + 1;
            Write(edited);
            Console.WriteLine($"data.json edited on disk; now serial {edited.Serial}.");
        }
    }

    private void Write(ServerData data)
    {
        var text = JsonSerializer.Serialize(data, StoreJsonContext.Default.ServerData);
        var temp = _dataPath + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, _dataPath, overwrite: true);
        _data = Clone(data);
        _dataHash = Hash(text);
    }

    private static void WriteSecret(string path, string text)
    {
        File.WriteAllText(path, text);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static ServerData Deserialize(string text)
    {
        var d = JsonSerializer.Deserialize(text, StoreJsonContext.Default.ServerData) ?? new ServerData();
        d.Groups = new Dictionary<string, JsonElement>(d.Groups, StringComparer.OrdinalIgnoreCase);
        d.Machines = d.Machines.ToDictionary(kv => ServerConfig.NormalizeHost(kv.Key), kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        return d;
    }

    /// <summary>Case-insensitive group names, short lower-case hostnames (for data from the web UI).</summary>
    public static ServerData Normalize(ServerData d) => Clone(d);

    private static ServerData Clone(ServerData d) =>
        Deserialize(JsonSerializer.Serialize(d, StoreJsonContext.Default.ServerData));

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public void Dispose()
    {
        _watcher?.Dispose();
        Key.Dispose();
    }
}

public sealed record AdminRecord(string Salt, string Hash, int Iterations);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
    WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ServerData))]
[JsonSerializable(typeof(AdminRecord))]
internal sealed partial class StoreJsonContext : JsonSerializerContext;
