using System.Globalization;

namespace NetBanner.Core;

/// <summary>Tolerant readers for raw registry-style values (DWORDs may arrive as int, long, or string).</summary>
internal static class RawValues
{
    public static Dictionary<string, object?> CaseInsensitive(IReadOnlyDictionary<string, object?> values) =>
        new(values, StringComparer.OrdinalIgnoreCase);

    public static int? GetInt(Dictionary<string, object?> v, string name) =>
        v.TryGetValue(name, out var raw) ? raw switch
        {
            int i => i,
            long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
            uint u when u <= int.MaxValue => (int)u,
            string s when int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) => p,
            _ => null,
        } : null;

    public static string? GetString(Dictionary<string, object?> v, string name) =>
        v.TryGetValue(name, out var raw) ? raw switch
        {
            string s => s,
            string[] arr => string.Join(' ', arr),
            null => null,
            _ => Convert.ToString(raw, CultureInfo.InvariantCulture),
        } : null;

    public static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
