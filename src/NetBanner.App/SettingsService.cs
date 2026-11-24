using System.Net.NetworkInformation;
using NetBanner.Core;
using NetBanner.Windows;

namespace NetBanner.App;

/// <summary>Resolves settings from all sources and reports when they change.</summary>
internal sealed class SettingsService : IDisposable
{
    private readonly AppOptions _options;
    private readonly PolicyChangeMonitor? _policyMonitor;
    private readonly ConfigFileWatcher _configWatcher;
    private readonly ConfigFileWatcher _serverWatcher;
    private readonly object _reloadLock = new();
    private string? _lastSummary;

    public BannerSettings Current { get; private set; } = BannerSettings.Default;

    public string ConfigPath { get; }

    /// <summary>Raised on a background thread when settings change, or when token values (e.g. {ip}) may have changed.</summary>
    public event Action<BannerSettings>? Changed;

    public SettingsService(AppOptions options)
    {
        _options = options;
        ConfigPath = options.ConfigPath ?? DefaultConfigPath;

        if (!options.NoPolicy)
        {
            _policyMonitor = new PolicyChangeMonitor();
            _policyMonitor.Changed += (_, _) => ReloadAndNotify("Policy change detected");
        }

        _configWatcher = new ConfigFileWatcher(ConfigPath);
        _configWatcher.Changed += (_, _) => ReloadAndNotify("Config file change detected");

        // Written by the SYSTEM sync task when this machine is pointed at a NetBanner server.
        _serverWatcher = new ConfigFileWatcher(Path.Combine(ConfigStore.StateDir, ServerSync.CacheFile));
        _serverWatcher.Changed += (_, _) => ReloadAndNotify("Server config change detected");

        Reload();
    }

    public static string DefaultConfigPath => ConfigStore.DefaultPath;

    public void Start()
    {
        _policyMonitor?.Start();
        _configWatcher.Start();
        _serverWatcher.Start();
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => Changed?.Invoke(Current); // refresh {ip}

    private void ReloadAndNotify(string reason)
    {
        Log.Info($"{reason}; reloading settings.");
        Reload();
        Changed?.Invoke(Current);
    }

    private void Reload()
    {
        lock (_reloadLock)
        {
            var warnings = new List<string>();

            // Highest precedence first.
            PartialSettings?[] raw = _options.NoPolicy
                ? [null, null]
                : [PolicySource.Read(), LegacyPolicySource.Read()];
            if (_options.LegacyValues is { } overrides)
            {
                raw[1] = LegacyPolicyMapper.Map(overrides);
            }

            // Group Policy > NetBanner server (only when the local file points at one) > local file.
            var local = ConfigFile.Load(ConfigPath, warnings);
            var server = local?.ServerUrl is null ? null : ServerSync.LoadCached(ConfigStore.StateDir, warnings);
            var sources = raw.Append(server).Append(local)
                .OfType<PartialSettings>()
                .Select(s => SettingsValidator.Normalize(s, warnings))
                .ToList();

            var result = SettingsMerger.Merge(sources);
            Current = result.Settings;

            foreach (var w in warnings)
            {
                Log.Warn(w);
            }

            var view = BannerPresenter.Present(Current, Tokens.FromSystem);
            var summary = $"Effective banner: \"{view.CenterText}\" " +
                          $"left=[{string.Join(", ", view.Left.Select(i => i.Text))}] " +
                          $"right=[{string.Join(", ", view.Right.Select(i => i.Text))}] " +
                          $"height={Current.Height} font={Current.FontSize} fullScreen={Current.FullScreenBehavior}. " +
                          $"Sources: {string.Join(", ", result.Provenance.Select(p => $"{p.Key}={p.Value}"))}";
            if (summary != _lastSummary)
            {
                Log.Info(summary);
                _lastSummary = summary;
            }
        }
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        _policyMonitor?.Dispose();
        _configWatcher.Dispose();
        _serverWatcher.Dispose();
    }
}
