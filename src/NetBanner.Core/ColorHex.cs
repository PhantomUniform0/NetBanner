namespace NetBanner.Core;

public static class ColorHex
{
    /// <summary>Accepts <c>#RGB</c>, <c>#RRGGBB</c>, or the same without <c>#</c>. Returns canonical <c>#RRGGBB</c>.</summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        var s = input?.Trim().TrimStart('#');
        if (s is null || !s.All(Uri.IsHexDigit))
        {
            return false;
        }

        if (s.Length == 3)
        {
            s = string.Concat(s.Select(c => $"{c}{c}"));
        }

        if (s.Length != 6)
        {
            return false;
        }

        normalized = "#" + s.ToUpperInvariant();
        return true;
    }
}
