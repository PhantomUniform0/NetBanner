using NetBanner.Core;

namespace NetBanner.Linux;

/// <summary>
/// Settings on Linux: the NetBanner server's config (if this machine is pointed at one) above the admin-managed file
/// <c>/etc/netbanner/config.json</c> (same format as Windows). Nothing usable gives the safe default:
/// CLASSIFICATION NOT CONFIGURED.
/// </summary>
internal static class LinuxSettings
{
    public const string DefaultPath = "/etc/netbanner/config.json";

    /// <summary>Server sync state (pinned key, cached config). Written by root's sync service, readable by everyone.</summary>
    public static string StateDir => Environment.GetEnvironmentVariable("NETBANNER_STATE") ?? "/var/lib/netbanner";

    public static string ServerCachePath => Path.Combine(StateDir, ServerSync.CacheFile);

    /// <summary>
    /// Linux default height. Linux fonts give 13px text a taller line box than Windows (Segoe UI), so 14px clips it;
    /// 16px fits. Windows keeps 14px. An explicit "height" in the config file still wins.
    /// </summary>
    public const int DefaultHeight = 16;

    /// <summary><c>--config</c> beats the <c>NETBANNER_CONFIG</c> environment variable, which beats the default.</summary>
    public static string ResolvePath(string? fromArgs) =>
        fromArgs ?? Environment.GetEnvironmentVariable("NETBANNER_CONFIG") ?? DefaultPath;

    /// <summary>The server URL from the local config file, or null.</summary>
    public static string? ServerUrl(string path) => ConfigFile.Load(path, new List<string>())?.ServerUrl;

    public static BannerSettings Load(string path, List<string> warnings)
    {
        var sources = new List<PartialSettings>();
        var local = ConfigFile.Load(path, warnings);

        // Highest first: server (only when this machine is pointed at one), local file, Linux default.
        if (local?.ServerUrl is not null && ServerSync.LoadCached(StateDir, warnings) is { } server)
        {
            sources.Add(SettingsValidator.Normalize(server, warnings));
        }

        if (local is not null)
        {
            sources.Add(SettingsValidator.Normalize(local, warnings));
        }

        sources.Add(new PartialSettings("LinuxDefault") { Height = DefaultHeight }); // lowest precedence
        return SettingsMerger.Merge(sources).Settings;
    }
}
