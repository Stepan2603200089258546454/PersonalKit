namespace Common.Security.OpenIddict.Server;

/// <summary>
/// Настройки сервера OpenID Connect, которые не зависят от конкретного приложения.
/// </summary>
/// <remarks>
/// Значения по умолчанию рассчитаны на домашний сервер: access-токен живёт час,
/// refresh-токен — две недели, код устройства — десять минут. Приложение может
/// переопределить их в <c>appsettings.json</c> (секция <c>OpenIddict</c>) или
/// в делегате настройки, переданном в <see cref="OpenIddictServerExtensions"/>.
/// </remarks>
public sealed class OpenIddictServerOptions
{
    /// <summary>Публичный адрес издателя токенов. Пусто — адрес текущего запроса.</summary>
    public string? Issuer { get; set; }

    /// <summary>Разрешить работу по HTTP. Только для локальной сети и разработки.</summary>
    public bool AllowHttp { get; set; }

    /// <summary>Время жизни access-токена.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Время жизни refresh-токена.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Время жизни кода устройства.</summary>
    public TimeSpan DeviceCodeLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Каталог с PFX-сертификатами подписи и шифрования. Пусто — ключи в памяти.</summary>
    public string? CertificatesDirectory { get; set; }

    /// <summary>Пароль PFX-сертификатов.</summary>
    public string? CertificatesPassword { get; set; }

    /// <summary>Не шифровать access-токен, чтобы API мог прочитать его без ключей сервера.</summary>
    public bool DisableAccessTokenEncryption { get; set; } = true;

    /// <summary>Включить сценарий client_credentials для сервисных клиентов.</summary>
    public bool EnableClientCredentialsFlow { get; set; } = true;

    /// <summary>
    /// Дополнительные области, которые сервер готов выдать. Обязательные области OIDC
    /// (<c>openid</c>, <c>profile</c>, <c>email</c>, <c>offline_access</c>) добавляются
    /// библиотекой автоматически.
    /// </summary>
    public IList<string> AdditionalScopes { get; } = new List<string>();
}
