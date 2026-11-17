namespace Common.Security.OpenIddict.Server.Security;

/// <summary>Типы событий журнала безопасности.</summary>
public static class UserSecurityEventTypes
{
    /// <summary>Успешный вход.</summary>
    public const string SignIn = "SignIn";

    /// <summary>Вход отклонён политикой или блокировкой устройства.</summary>
    public const string SignInRejected = "SignInRejected";

    /// <summary>Выход пользователя.</summary>
    public const string SignOut = "SignOut";

    /// <summary>Приложение получило доступ в рамках сеанса.</summary>
    public const string ApplicationAuthorized = "ApplicationAuthorized";

    /// <summary>Сеанс отозван.</summary>
    public const string SessionRevoked = "SessionRevoked";

    /// <summary>Устройство заблокировано.</summary>
    public const string DeviceBlocked = "DeviceBlocked";

    /// <summary>Устройство разблокировано.</summary>
    public const string DeviceUnblocked = "DeviceUnblocked";

    /// <summary>Пользователь изменил политику входа.</summary>
    public const string PolicyChanged = "PolicyChanged";

    /// <summary>Все сеансы пользователя завершены.</summary>
    public const string AllSessionsRevoked = "AllSessionsRevoked";
}

/// <summary>Настройки безопасности пользователя.</summary>
public sealed record UserSecuritySettingsDto(
    DeviceLoginPolicy DevicePolicy,
    DateTimeOffset UpdatedAt);

/// <summary>Приложение в рамках сеанса.</summary>
public sealed record SessionApplicationSummary(
    string ClientId,
    string? ApplicationName,
    IReadOnlyList<string> Scopes,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

/// <summary>Сеанс пользователя для интерфейса.</summary>
public sealed record SessionSummary(
    Guid Id,
    string UserId,
    Guid DeviceId,
    string DeviceName,
    string? Browser,
    string? OperatingSystem,
    string? DeviceType,
    string? IpAddress,
    string? Location,
    string ClientId,
    string? ApplicationName,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    string? RevokedBy,
    string? RevokeReason,
    bool IsActive,
    bool DeviceIsBlocked,
    IReadOnlyList<SessionApplicationSummary> Applications);

/// <summary>Устройство пользователя для интерфейса.</summary>
public sealed record DeviceSummary(
    Guid Id,
    string UserId,
    string DisplayName,
    string? Browser,
    string? OperatingSystem,
    string? DeviceType,
    string? FirstIpAddress,
    string? LastIpAddress,
    string? LastLocation,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    bool IsBlocked,
    DateTimeOffset? BlockedAt,
    string? BlockedBy,
    string? BlockReason,
    int ActiveSessionCount);

/// <summary>Событие журнала безопасности.</summary>
public sealed record LoginAuditSummary(
    Guid Id,
    string UserId,
    string EventType,
    Guid? SessionId,
    Guid? DeviceId,
    string? DeviceName,
    string? ClientId,
    string? ApplicationName,
    string? IpAddress,
    string? Location,
    string? UserAgent,
    string? Details,
    DateTimeOffset OccurredAt);

/// <summary>Результат проверки политики перед входом.</summary>
public enum SignInPolicyStatus
{
    /// <summary>Вход разрешён.</summary>
    Allowed = 0,

    /// <summary>Устройство заблокировано.</summary>
    DeviceBlocked = 1,

    /// <summary>Нужно завершить сеанс на другом устройстве.</summary>
    RequiresOldSessionEnd = 2
}

/// <summary>Решение политики входа.</summary>
public sealed record SignInPolicyDecision(
    SignInPolicyStatus Status,
    string? Message,
    IReadOnlyList<SessionSummary> ConflictingSessions)
{
    /// <summary>Вход разрешён.</summary>
    public bool IsAllowed => Status == SignInPolicyStatus.Allowed;
}

/// <summary>Результат создания сеанса.</summary>
public sealed record SessionStartResult(
    SignInPolicyStatus Status,
    Guid? SessionId,
    string? Message,
    IReadOnlyList<SessionSummary> ConflictingSessions)
{
    /// <summary>Сеанс создан.</summary>
    public bool IsStarted => Status == SignInPolicyStatus.Allowed && SessionId is not null;
}

/// <summary>Результат проверки сеанса при очередном запросе.</summary>
public sealed record SessionValidationResult(bool IsActive, string? Reason);
