using System.Reflection;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using NetBanner.Core;
using NetBanner.Server;

// netbanner-server: central NetBanner config for machines Group Policy doesn't reach.
//   netbanner-server                 run (http://0.0.0.0:8080; override with --urls or ASPNETCORE_URLS)
//   netbanner-server set-password    set the web UI admin password (reads it from stdin)
//   netbanner-server fingerprint     print the signing key fingerprint clients will pin
// Data folder: NETBANNER_SERVER_DATA, else /var/lib/netbanner-server or %ProgramData%\NetBannerServer.

var dataDir = Environment.GetEnvironmentVariable("NETBANNER_SERVER_DATA") ?? (OperatingSystem.IsWindows()
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetBannerServer")
    : "/var/lib/netbanner-server");
var version = typeof(ServerStore).Assembly.GetName().Version?.ToString(3) ?? "?";

// On Linux the data folder belongs to the service user; files root creates there (0600) would lock the service out.
if (!OperatingSystem.IsWindows() && Environment.UserName == "root" && args.FirstOrDefault() is "set-password" or "fingerprint" or null)
{
    Console.Error.WriteLine("Don't run netbanner-server as root; run it as the service user, e.g.:");
    Console.Error.WriteLine($"  sudo -u netbanner-server netbanner-server {args.FirstOrDefault() ?? string.Empty}".TrimEnd());
    return 1;
}

using var store = new ServerStore(dataDir);

switch (args.FirstOrDefault())
{
    case "set-password":
        Console.Error.Write("New admin password: ");
        var password = Console.ReadLine();
        if (string.IsNullOrEmpty(password) || password.Length < 8)
        {
            Console.Error.WriteLine("Password must be at least 8 characters.");
            return 1;
        }

        store.SetAdminPassword(password);
        Console.Error.WriteLine("Admin password set.");
        return 0;
    case "fingerprint":
        Console.WriteLine(store.Fingerprint);
        return 0;
}

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrEmpty(builder.Configuration["urls"]) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://0.0.0.0:8080");
}

builder.Services.AddSingleton(store);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default));
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "session-keys")));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "netbanner-admin";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.None; // plain HTTP by decision
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

var indexHtml = ReadResource("index.html");
app.MapGet("/", () => Results.Content(indexHtml, "text/html; charset=utf-8"));

// Vendored Clarity CSS and fonts (ui/vendor), embedded. Only names present in the assembly can be served.
var vendor = Assembly.GetExecutingAssembly().GetManifestResourceNames()
    .Where(n => n.StartsWith("vendor/", StringComparison.Ordinal))
    .ToDictionary(n => n.Replace('\\', '/'), n => n, StringComparer.Ordinal);
app.MapGet("/vendor/{**path}", (string path, HttpContext ctx) =>
{
    if (!vendor.TryGetValue("vendor/" + path, out var resource))
    {
        return Results.NotFound();
    }

    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    var type = Path.GetExtension(path) switch { ".css" => "text/css; charset=utf-8", ".woff2" => "font/woff2", _ => "application/octet-stream" };
    return Results.Stream(Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)!, type);
});

// ---- Clients (no auth: they only download signed configs) ----
app.MapGet("/api/config/{host}", (string host, HttpContext ctx, ServerStore s) =>
{
    s.EnsureMachine(host);
    var etag = $"\"{s.Serial}\"";
    if (ctx.Request.Headers.IfNoneMatch.ToString() == etag)
    {
        return Results.StatusCode(StatusCodes.Status304NotModified);
    }

    ctx.Response.Headers.ETag = etag;
    return Results.Json(s.ConfigFor(host), ServerJsonContext.Default.SignedConfig);
});

app.MapGet("/api/info", (ServerStore s) => new InfoResponse(version, s.Fingerprint, s.HasAdminPassword));

// ---- Web UI ----
app.MapPost("/api/login", async (LoginRequest req, HttpContext ctx, ServerStore s) =>
{
    if (!s.CheckAdminPassword(req.Password ?? string.Empty))
    {
        await Task.Delay(1000); // slow down guessing
        return Results.Unauthorized();
    }

    var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(new ClaimsPrincipal(identity));
    return Results.Ok();
});

app.MapPost("/api/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync();
    return Results.Ok();
});

var admin = app.MapGroup("/api/admin").RequireAuthorization();

admin.MapGet("/data", (ServerStore s) => s.Snapshot());

admin.MapPut("/data", (ServerData data, ServerStore s) =>
{
    data = ServerStore.Normalize(data);
    var problems = Validate(data);
    if (problems.Count > 0)
    {
        // Nothing invalid is ever saved (clients would silently drop the bad parts). Nothing changes.
        return Results.Json(new SaveResponse(s.Serial, problems), ApiJsonContext.Default.SaveResponse, statusCode: StatusCodes.Status400BadRequest);
    }

    return Results.Json(new SaveResponse(s.Save(data), problems), ApiJsonContext.Default.SaveResponse);
});

admin.MapPost("/preview", (PreviewRequest request) =>
{
    var req = request with { Data = ServerStore.Normalize(request.Data) };
    var warnings = new List<string>();
    List<PartialSettings?> layers;
    Func<string, string?>? tokens = null;
    switch (req.Kind)
    {
        case "machine":
            var host = req.Name ?? string.Empty;
            layers = ServerStore.Layers(req.Data, host, warnings);
            tokens = t => t == "hostname" ? host : null;
            break;
        case "group" when req.Name is not null && req.Data.Groups.TryGetValue(req.Name, out var g):
            layers = [ServerStore.ParseLayer(g, $"group {req.Name}", warnings), ServerStore.ParseLayer(req.Data.Default, "default", warnings)];
            break;
        default:
            layers = [ServerStore.ParseLayer(req.Data.Default, "default", warnings)];
            break;
    }

    var merged = SettingsMerger.Merge(layers);
    var view = BannerPresenter.Present(merged.Settings, tokens);
    return new PreviewResponse(view.CenterText, view.Background, view.Foreground,
        view.Left.Select(i => new PreviewItem(i.Text, i.Background, i.Foreground)).ToList(),
        view.Right.Select(i => new PreviewItem(i.Text, i.Background, i.Foreground)).ToList(),
        merged.Settings.Height, merged.Settings.FontSize,
        merged.Provenance.GetValueOrDefault(nameof(BannerSettings.Classification)) ?? SettingsMerger.DefaultSource,
        warnings);
});

admin.MapGet("/meta", () => new MetaResponse(
    MarkingCatalog.All.Select(m => new MarkingInfo(m.Id, m.Text, m.Background, m.Foreground)).ToList(),
    SettingsValidator.FpconLevels.ToList(),
    SettingsValidator.MinHeight, SettingsValidator.MaxHeight, SettingsValidator.MinFontSize, SettingsValidator.MaxFontSize));

Console.WriteLine($"NetBanner server {version}. Data: {dataDir}. Signing key: {store.Fingerprint}.");
if (!store.HasAdminPassword)
{
    Console.WriteLine("No admin password yet: run 'netbanner-server set-password' to use the web UI.");
}

app.Run();
return 0;

static List<string> Validate(ServerData data)
{
    var warnings = new List<string>();
    ServerStore.ParseLayer(data.Default, "default", warnings);
    foreach (var (name, g) in data.Groups)
    {
        ServerStore.ParseLayer(g, $"group {name}", warnings);
    }

    foreach (var (host, m) in data.Machines)
    {
        ServerStore.ParseLayer(m.Settings, $"machine {host}", warnings);
        if (!string.IsNullOrWhiteSpace(m.Group) && !data.Groups.ContainsKey(m.Group))
        {
            warnings.Add($"machine {host}: group '{m.Group}' does not exist.");
        }
    }

    return warnings;
}

static string ReadResource(string name)
{
    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

public sealed record InfoResponse(string Version, string Fingerprint, bool HasAdminPassword);
public sealed record LoginRequest(string? Password);
public sealed record SaveResponse(long Serial, List<string> Warnings);
public sealed record PreviewRequest(ServerData Data, string Kind, string? Name);
public sealed record PreviewItem(string Text, string? Background, string? Foreground);
public sealed record PreviewResponse(string Text, string Background, string Foreground, List<PreviewItem> Left,
    List<PreviewItem> Right, int Height, double FontSize, string MarkingFrom, List<string> Warnings);
public sealed record MarkingInfo(string Id, string Text, string Background, string Foreground);
public sealed record MetaResponse(List<MarkingInfo> Markings, List<string> FpconLevels, int MinHeight, int MaxHeight,
    double MinFontSize, double MaxFontSize);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InfoResponse))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(SaveResponse))]
[JsonSerializable(typeof(PreviewRequest))]
[JsonSerializable(typeof(PreviewResponse))]
[JsonSerializable(typeof(MetaResponse))]
[JsonSerializable(typeof(ServerData))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
