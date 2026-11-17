namespace Common.Security.OpenIddict.Client.Web;

/// <summary>
/// Настройки проверки access-токенов в API.
/// </summary>
public sealed class OpenIddictApiValidationOptions
{
    /// <summary>Issuer Identity. Должен совпадать с токеном.</summary>
    public string Issuer { get; set; } = "https://localhost:7163/";

    /// <summary>Клиент, которому разрешён introspection.</summary>
    public string ClientId { get; set; } = "devhome-api";

    /// <summary>Секрет клиента introspection.</summary>
    public string ClientSecret { get; set; } = string.Empty;
}
