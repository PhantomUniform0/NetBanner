using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace NetBanner.Core;

/// <summary>
/// Expands <c>{token}</c> placeholders in custom text. Supported: <c>{hostname}</c>, <c>{username}</c>,
/// <c>{domain}</c>, <c>{ip}</c>. Unknown tokens are left as typed.
/// </summary>
public static partial class Tokens
{
    public static IReadOnlyList<string> Supported { get; } = ["hostname", "username", "domain", "ip"];

    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial Regex TokenPattern();

    public static string Expand(string text, Func<string, string?>? resolve)
    {
        if (resolve is null || text.IndexOf('{') < 0)
        {
            return text;
        }

        return TokenPattern().Replace(text, m => resolve(m.Groups[1].Value.ToLowerInvariant()) ?? m.Value);
    }

    /// <summary>Resolves tokens from the running system.</summary>
    public static string? FromSystem(string name)
    {
        try
        {
            return name switch
            {
                "hostname" => Environment.MachineName,
                "username" => Environment.UserName,
                "domain" => Environment.UserDomainName,
                "ip" => PrimaryIPv4() ?? "NO IP",
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>First IPv4 address on an up, non-loopback interface that has a default gateway (falls back to any).</summary>
    private static string? PrimaryIPv4()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => n.GetIPProperties())
            .OrderByDescending(p => p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .SelectMany(p => p.UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork);
        return candidates.FirstOrDefault()?.ToString();
    }
}
