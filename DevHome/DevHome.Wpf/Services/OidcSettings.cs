using System.IO;
using System.Text.Json;

namespace DevHome.Wpf.Services;

/// <summary>
/// Настройки OIDC-клиента из appsettings.json.
/// </summary>
public sealed record OidcSettings(
    string Authority,
    string ClientId,
    string RedirectUri,
    string Scope)
{
    public static OidcSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var json = File.Exists(path) ? File.ReadAllText(path) : "{}";

        using var document = JsonDocument.Parse(json);
        var section = document.RootElement.TryGetProperty("Authentication", out var auth) &&
                      auth.TryGetProperty("DevHome", out var devHome)
            ? devHome
            : default;

        return new OidcSettings(
            ReadString(section, "Authority") ?? "http://127.0.0.1:5201/",
            ReadString(section, "ClientId") ?? "devhome-wpf",
            ReadString(section, "RedirectUri") ?? "http://127.0.0.1:5181/callback",
            ReadString(section, "Scope") ?? "openid profile email offline_access devhome.api");
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.GetString()
            : null;
}
