namespace Common.Security.OpenIddict.Server.Security;

/// <summary>
/// Настройки подсистемы устройств и сеансов.
/// </summary>
public sealed class UserSecurityOptions
{
    /// <summary>Имя постоянной cookie, по которой узнаётся устройство.</summary>
    public string DeviceCookieName { get; set; } = "DevHome.Device";

    /// <summary>Срок жизни cookie устройства.</summary>
    public TimeSpan DeviceCookieLifetime { get; set; } = TimeSpan.FromDays(730);

    /// <summary>Срок жизни сеанса, если он не продлевается.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>ClientId, под которым в журнале отмечается сама Identity.</summary>
    public string IdentityClientId { get; set; } = "devhome-identity";

    /// <summary>Название Identity в журнале.</summary>
    public string IdentityApplicationName { get; set; } = "DevHome.Identity";

    /// <summary>Сколько дней хранить журнал событий.</summary>
    public TimeSpan AuditRetention { get; set; } = TimeSpan.FromDays(180);

    /// <summary>Как часто обновлять «последний раз замечен» у сеанса.</summary>
    public TimeSpan TouchInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Доверять <c>X-Forwarded-For</c> и геозаголовкам. Включайте только за
    /// доверенным reverse proxy: иначе клиент подделает IP и локацию.
    /// </summary>
    public bool TrustForwardedHeaders { get; set; }
}
