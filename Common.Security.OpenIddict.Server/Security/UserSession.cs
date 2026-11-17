namespace Common.Security.OpenIddict.Server.Security;

/// <summary>
/// Один вход пользователя: от успешной аутентификации до выхода или отзыва.
/// </summary>
/// <remarks>
/// Сеанс — это запись в базе, а не cookie. Cookie Identity содержит идентификатор
/// сеанса (<c>sid</c>), поэтому сервер может проверить, что сеанс ещё действует,
/// при каждом запросе. Клиентские приложения получают тот же <c>sid</c> в токене.
/// </remarks>
public sealed class UserSession
{
    /// <summary>Идентификатор сеанса.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Пользователь, вошедший в систему.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Устройство, с которого выполнен вход.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Устройство, с которого выполнен вход.</summary>
    public UserDevice? Device { get; set; }

    /// <summary>
    /// Приложение, инициировавшее вход: <c>devhome-identity</c> для самой Identity
    /// или ClientId OIDC-клиента, например <c>devhome-web</c>.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Понятное название приложения на момент входа.</summary>
    public string? ApplicationName { get; set; }

    /// <summary>Когда сеанс начался.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Когда сеанс последний раз замечен.</summary>
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Когда сеанс истекает сам, без явного отзыва.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Когда сеанс отозван.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Кто отозвал сеанс: сам пользователь, администратор или система.</summary>
    public string? RevokedBy { get; set; }

    /// <summary>Причина отзыва.</summary>
    public string? RevokeReason { get; set; }

    /// <summary>IP-адрес входа.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Локация входа.</summary>
    public string? Location { get; set; }

    /// <summary>User agent браузера на момент входа.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Приложения, в которые пользователь входил в рамках сеанса.</summary>
    public ICollection<UserSessionApplication> Applications { get; set; } = new List<UserSessionApplication>();

    /// <summary>Сеанс ещё действует.</summary>
    public bool IsActive => RevokedAt is null && (ExpiresAt is null || ExpiresAt > DateTimeOffset.UtcNow);
}
