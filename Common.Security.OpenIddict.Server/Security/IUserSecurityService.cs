namespace Common.Security.OpenIddict.Server.Security;

/// <summary>
/// Хранит сеансы, устройства, настройки и журнал безопасности пользователя.
/// </summary>
public interface IUserSecurityService
{
    /// <summary>Возвращает настройки пользователя или значения по умолчанию.</summary>
    Task<UserSecuritySettingsDto> GetSettingsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Сохраняет политику одновременного входа.</summary>
    Task SetPolicyAsync(
        string userId,
        DeviceLoginPolicy policy,
        string actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Проверяет, можно ли начать новый сеанс, не создавая его. Используется
    /// страницей входа, чтобы показать понятное сообщение до установки cookie.
    /// </summary>
    Task<SignInPolicyDecision> EvaluateSignInAsync(
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken = default);

    /// <summary>Создаёт сеанс и применяет политику входа.</summary>
    Task<SessionStartResult> StartSessionAsync(
        string userId,
        Guid deviceId,
        string clientId,
        string? applicationName,
        RequestContextInfo request,
        string? authenticationMethod = null,
        CancellationToken cancellationToken = default);

    /// <summary>Проверяет, что сеанс ещё действует и устройство не заблокировано.</summary>
    Task<SessionValidationResult> ValidateSessionAsync(
        string userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Обновляет время последней активности, но не чаще заданного интервала.</summary>
    Task TouchSessionAsync(
        string userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Возвращает действующие сеансы пользователя.</summary>
    Task<IReadOnlyList<SessionSummary>> GetActiveSessionsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>Возвращает последние сеансы, включая завершённые.</summary>
    Task<IReadOnlyList<SessionSummary>> GetSessionHistoryAsync(
        string userId,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>Возвращает устройства пользователя.</summary>
    Task<IReadOnlyList<DeviceSummary>> GetDevicesAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>Возвращает журнал событий безопасности.</summary>
    Task<IReadOnlyList<LoginAuditSummary>> GetAuditAsync(
        string userId,
        int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>Возвращает действующие сеансы всех пользователей (для админки).</summary>
    Task<IReadOnlyList<SessionSummary>> GetAllActiveSessionsAsync(
        int take = 200,
        CancellationToken cancellationToken = default);

    /// <summary>Возвращает последние события безопасности всех пользователей (для админки).</summary>
    Task<IReadOnlyList<LoginAuditSummary>> GetAllAuditAsync(
        int take = 200,
        CancellationToken cancellationToken = default);

    /// <summary>Записывает вход приложения в рамках сеанса.</summary>
    Task RecordApplicationAsync(
        string userId,
        Guid sessionId,
        string clientId,
        string? applicationName,
        IEnumerable<string> scopes,
        RequestContextInfo request,
        CancellationToken cancellationToken = default);

    /// <summary>Отзывает один сеанс и связанные с ним токены приложений.</summary>
    Task RevokeSessionAsync(
        string userId,
        Guid sessionId,
        string actorUserId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>Отзывает все сеансы пользователя.</summary>
    Task RevokeAllSessionsAsync(
        string userId,
        string actorUserId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>Блокирует устройство и отзывает все его сеансы.</summary>
    Task BlockDeviceAsync(
        string userId,
        Guid deviceId,
        string actorUserId,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>Снимает блокировку устройства. Старые сеансы не восстанавливаются.</summary>
    Task UnblockDeviceAsync(
        string userId,
        Guid deviceId,
        string actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Удаляет все данные безопасности пользователя при удалении учётной записи.</summary>
    Task DeleteUserDataAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Удаляет записи журнала старше срока хранения.</summary>
    Task<int> CleanupAuditAsync(CancellationToken cancellationToken = default);
}
