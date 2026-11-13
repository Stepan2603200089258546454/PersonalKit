namespace DevHome.Maui.Services;

/// <summary>
/// Настройки OIDC-клиента. Значения можно переопределить переменными окружения.
/// </summary>
public sealed record OidcSettings(
    string Authority,
    string ClientId,
    string RedirectUri,
    string Scope)
{
    public static OidcSettings Load()
    {
        return new OidcSettings(
            Environment.GetEnvironmentVariable("DEVHOME_AUTHORITY") ?? "https://localhost:7163/",
            Environment.GetEnvironmentVariable("DEVHOME_CLIENT_ID") ?? "devhome-maui",
            Environment.GetEnvironmentVariable("DEVHOME_REDIRECT_URI") ?? "http://127.0.0.1:5180/callback",
            Environment.GetEnvironmentVariable("DEVHOME_SCOPE") ?? "openid profile email offline_access devhome.api");
    }
}
