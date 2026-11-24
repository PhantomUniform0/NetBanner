using System.Globalization;

namespace NetBanner.App;

/// <summary>Command-line switches. Intended for testing and troubleshooting, not normal deployment.</summary>
internal sealed record AppOptions
{
    /// <summary><c>--no-harden</c>: don't protect the process from being ended by standard users.</summary>
    public bool NoHarden { get; init; }

    /// <summary><c>--exit-after &lt;seconds&gt;</c>: quit automatically (for tests and screenshots).</summary>
    public int? ExitAfterSeconds { get; init; }

    /// <summary>
    /// <c>--legacy-values "Classification=2;CaveatsEnabled=1;Caveats=NOFORN;FpCon=1"</c>: use these values
    /// instead of the legacy registry key. Implies <c>--no-policy</c>.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? LegacyValues { get; init; }

    /// <summary><c>--log &lt;file&gt;</c>: also append log messages to this file (troubleshooting).</summary>
    public string? LogFile { get; init; }

    /// <summary><c>--config &lt;file&gt;</c>: use this config file instead of %ProgramData%\NetBanner\config.json.</summary>
    public string? ConfigPath { get; init; }

    /// <summary><c>--no-policy</c>: ignore both GPO registry keys (test the config file on its own).</summary>
    public bool NoPolicy { get; init; }

    public static AppOptions Parse(string[] args)
    {
        var o = new AppOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--no-harden":
                    o = o with { NoHarden = true };
                    break;
                case "--exit-after" when i + 1 < args.Length
                    && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) && s > 0:
                    o = o with { ExitAfterSeconds = s };
                    i++;
                    break;
                case "--log" when i + 1 < args.Length:
                    o = o with { LogFile = args[++i] };
                    break;
                case "--legacy-values" when i + 1 < args.Length:
                    o = o with { LegacyValues = ParseValues(args[++i]), NoPolicy = true };
                    break;
                case "--config" when i + 1 < args.Length:
                    o = o with { ConfigPath = args[++i] };
                    break;
                case "--no-policy":
                    o = o with { NoPolicy = true };
                    break;
            }
        }

        return o;
    }

    private static Dictionary<string, object?> ParseValues(string spec)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var value = pair[(eq + 1)..];
            d[pair[..eq].Trim()] = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : value;
        }

        return d;
    }
}
