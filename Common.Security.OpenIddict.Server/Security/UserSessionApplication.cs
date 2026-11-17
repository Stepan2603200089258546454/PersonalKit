namespace Common.Security.OpenIddict.Server.Security;

/// <summary>
/// Клиентское приложение, в которое пользователь входил в рамках сеанса.
/// </summary>
/// <remarks>
/// Один сеанс Identity может использоваться несколькими приложениями: пользователь
/// вошёл на сайт, затем открыл DevHome, MAUI или CLI. Запись хранит, когда каждое
/// приложение впервые и последний раз обращалось к Identity, и какие области
/// запрашивало. Это позволяет показать «с какого приложения» выполнен вход.
/// </remarks>
public sealed class UserSessionApplication
{
    /// <summary>Идентификатор записи.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Сеанс, к которому относится вход в приложение.</summary>
    public Guid SessionId { get; set; }

    /// <summary>Сеанс, к которому относится вход в приложение.</summary>
    public UserSession? Session { get; set; }

    /// <summary>ClientId приложения.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Понятное название приложения.</summary>
    public string? ApplicationName { get; set; }

    /// <summary>Области, запрошенные приложением, через пробел.</summary>
    public string? Scopes { get; set; }

    /// <summary>Когда приложение впервые появилось в сеансе.</summary>
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Когда приложение последний раз обращалось к Identity.</summary>
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Когда доступ приложения к сеансу отозван.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
