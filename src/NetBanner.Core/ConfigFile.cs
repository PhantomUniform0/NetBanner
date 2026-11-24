using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetBanner.Core;

/// <summary>
/// The local machine config file (<c>config.json</c>), written by the config tool or by hand / Ansible / imaging.
/// Ranked below Group Policy. Comments and trailing commas are allowed.
/// </summary>
public static class ConfigFile
{
    public const string SourceName = "LocalConfig";

    /// <summary>Loads and parses the file. Missing file → null. Unreadable or invalid file → null with a warning.</summary>
    public static PartialSettings? Load(string path, ICollection<string> warnings)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string json;
        try
        {
            json = ReadShared(path);
        }
        catch (Exception ex)
        {
            warnings.Add($"{SourceName}: could not read {path}: {ex.Message}");
            return null;
        }

        return Parse(json, warnings);
    }

    public static PartialSettings? Parse(string json, ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        ConfigDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.ConfigDocument);
        }
        catch (JsonException ex)
        {
            warnings.Add($"{SourceName}: invalid JSON ({ex.Message}). File ignored.");
            return null;
        }

        if (doc is null)
        {
            return null;
        }

        CustomBanner? custom = doc.Custom is { } c
            ? new CustomBanner(c.Text ?? string.Empty, c.Background ?? "#FFFFFF", c.Foreground ?? "#000000")
            : null;

        FullScreenBehavior? fullScreen = null;
        if (doc.FullScreenBehavior is { } fsText)
        {
            if (Enum.TryParse<FullScreenBehavior>(fsText, true, out var fs))
            {
                fullScreen = fs;
            }
            else
            {
                warnings.Add($"{SourceName}: unknown fullScreenBehavior '{fsText}' (use StayOnTop or Yield).");
            }
        }

        var result = new PartialSettings(SourceName)
        {
            Classification = custom is null ? doc.Classification : null,
            Caveats = custom is null ? doc.Caveats : null,
            Custom = custom,
            Height = doc.Height,
            FontSize = doc.FontSize,
            LeftItems = Items(doc.LeftItems, "leftItems", warnings),
            RightItems = Items(doc.RightItems, "rightItems", warnings),
            FullScreenBehavior = fullScreen,
            ServerUrl = string.IsNullOrWhiteSpace(doc.Server?.Url) ? null : doc.Server.Url.Trim().TrimEnd('/'),
        };

        return result;
    }

    /// <summary>Serializes settings back to JSON (for the config tool).</summary>
    public static string Serialize(PartialSettings s)
    {
        var doc = new ConfigDocument
        {
            Classification = s.Classification,
            Caveats = s.Caveats,
            Custom = s.Custom is { } c ? new CustomDocument { Text = c.Text, Background = c.Background, Foreground = c.Foreground } : null,
            Height = s.Height,
            FontSize = s.FontSize,
            LeftItems = s.LeftItems?.Select(ToDoc).ToList(),
            RightItems = s.RightItems?.Select(ToDoc).ToList(),
            FullScreenBehavior = s.FullScreenBehavior?.ToString(),
            Server = s.ServerUrl is null ? null : new ServerDocument { Url = s.ServerUrl },
        };
        return JsonSerializer.Serialize(doc, ConfigJsonContext.Default.ConfigDocument);
    }

    private static SideItemDocument ToDoc(SideItem i) => new()
    {
        Type = i.Type.ToString(),
        Value = JsonSerializer.SerializeToElement(i.Value, ConfigJsonContext.Default.String),
        Background = i.Background,
        Foreground = i.Foreground,
    };

    private static List<SideItem>? Items(List<SideItemDocument>? docs, string field, ICollection<string> warnings)
    {
        if (docs is null)
        {
            return null;
        }

        var items = new List<SideItem>();
        foreach (var d in docs)
        {
            if (!Enum.TryParse<SideItemType>(d.Type, true, out var type))
            {
                warnings.Add($"{SourceName}: {field} item has unknown type '{d.Type}' (use Text, Cpcon or Fpcon).");
                continue;
            }

            string value = d.Value switch
            {
                { ValueKind: JsonValueKind.String } e => e.GetString() ?? string.Empty,
                { ValueKind: JsonValueKind.Number } e => e.GetRawText(),
                _ => string.Empty,
            };
            items.Add(new SideItem(type, value, d.Background, d.Foreground));
        }

        return items;
    }

    /// <summary>Reads while tolerating a writer that holds the file open (retries briefly).</summary>
    private static string ReadShared(string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(100 * attempt);
            }
        }
    }
}

internal sealed class ConfigDocument
{
    public string? Classification { get; set; }
    public string? Caveats { get; set; }
    public CustomDocument? Custom { get; set; }
    public int? Height { get; set; }
    public double? FontSize { get; set; }
    public List<SideItemDocument>? LeftItems { get; set; }
    public List<SideItemDocument>? RightItems { get; set; }
    public string? FullScreenBehavior { get; set; }
    public ServerDocument? Server { get; set; }
}

internal sealed class ServerDocument
{
    public string? Url { get; set; }
}

internal sealed class CustomDocument
{
    public string? Text { get; set; }
    public string? Background { get; set; }
    public string? Foreground { get; set; }
}

internal sealed class SideItemDocument
{
    public string? Type { get; set; }
    public JsonElement? Value { get; set; }
    public string? Background { get; set; }
    public string? Foreground { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ConfigDocument))]
[JsonSerializable(typeof(string))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext;
