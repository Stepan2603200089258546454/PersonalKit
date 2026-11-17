namespace Common.Security.OpenIddict.Server.Security;

/// <summary>
/// Журнал событий безопасности: входы, выходы, отзывы и блокировки.
/// </summary>
/// <remarks>
/// Журнал хранит не только текущие сеансы, но и историю. Записи не удаляются
/// при отзыве сеанса: пользователь и администратор должны видеть, когда и с какого
/// устройства выполнялся вход. Для очистки старых записей предусмотрен
/// <see cref="UserSecurityOptions.AuditRetention"/>.
/// </remarks>
public sealed class UserLoginAudit
{
    /// <summary>Идентификатор записи журнала.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Пользователь, к которому относится событие.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Тип события, например <c>SignIn</c> или <c>SessionRevoked</c>.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Сеанс, если событие с ним связано.</summary>
    public Guid? SessionId { get; set; }

    /// <summary>Устройство, если событие с ним связано.</summary>
    public Guid? DeviceId { get; set; }

    /// <summary>Название устройства на момент события.</summary>
    public string? DeviceName { get; set; }

    /// <summary>ClientId приложения.</summary>
    public string? ClientId { get; set; }

    /// <summary>Название приложения.</summary>
    public string? ApplicationName { get; set; }

    /// <summary>IP-адрес.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Локация.</summary>
    public string? Location { get; set; }

    /// <summary>User agent.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Подробности: причина отказа, список отозванных сеансов и т. п.</summary>
    public string? Details { get; set; }

    /// <summary>Когда событие произошло.</summary>
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
