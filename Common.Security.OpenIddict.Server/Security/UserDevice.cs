namespace Common.Security.OpenIddict.Server.Security;

/// <summary>
/// Устройство, с которого пользователь входил в приложение.
/// </summary>
/// <remarks>
/// <para>
/// Устройство определяется постоянной cookie браузера. Пока cookie сохраняется,
/// один и тот же браузер на одном компьютере считается одним устройством,
/// даже если обновилась версия браузера или изменился user agent.
/// </para>
/// <para>
/// В базе хранится не значение cookie, а его SHA-256-хеш: по украденной базе
/// нельзя восстановить cookie и выдать себя за устройство.
/// </para>
/// </remarks>
public sealed class UserDevice
{
    /// <summary>Идентификатор устройства.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Владелец устройства.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>SHA-256-хеш значения cookie устройства.</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>Понятное название, например «Chrome · Windows».</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Браузер, определённый по user agent.</summary>
    public string? Browser { get; set; }

    /// <summary>Операционная система, определённая по user agent.</summary>
    public string? OperatingSystem { get; set; }

    /// <summary>Тип устройства: компьютер, телефон, планшет.</summary>
    public string? DeviceType { get; set; }

    /// <summary>IP-адрес первого входа.</summary>
    public string? FirstIpAddress { get; set; }

    /// <summary>IP-адрес последнего входа.</summary>
    public string? LastIpAddress { get; set; }

    /// <summary>Локация последнего входа, если её сообщил прокси или определил IP.</summary>
    public string? LastLocation { get; set; }

    /// <summary>Последний известный user agent.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Когда устройство впервые появилось у пользователя.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Когда устройство последний раз использовалось.</summary>
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Устройство заблокировано: вход с него запрещён, сеансы отозваны.</summary>
    public bool IsBlocked { get; set; }

    /// <summary>Когда устройство заблокировано.</summary>
    public DateTimeOffset? BlockedAt { get; set; }

    /// <summary>Кто заблокировал: идентификатор пользователя или администратора.</summary>
    public string? BlockedBy { get; set; }

    /// <summary>Причина блокировки для журнала и интерфейса.</summary>
    public string? BlockReason { get; set; }

    /// <summary>Сеансы, открытые с устройства.</summary>
    public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();
}
