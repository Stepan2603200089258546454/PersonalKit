namespace Common.Security.OpenIddict.Server.Security;

/// <summary>Результат определения устройства по cookie.</summary>
/// <param name="Device">Найденное или созданное устройство.</param>
/// <param name="IsNew">Устройство появилось впервые.</param>
/// <param name="IsBlocked">Устройство заблокировано.</param>
public sealed record DeviceResolution(UserDevice Device, bool IsNew, bool IsBlocked);

/// <summary>
/// Определяет устройство по постоянной cookie и создаёт его при первом входе.
/// </summary>
public interface IDeviceIdentityService
{
    /// <summary>
    /// Возвращает устройство пользователя. Если cookie ещё нет, создаёт новую
    /// запись и устанавливает cookie в ответ.
    /// </summary>
    Task<DeviceResolution> ResolveAsync(
        string userId,
        HttpContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Находит устройство по cookie без создания новой записи.
    /// </summary>
    Task<UserDevice?> FindAsync(
        string userId,
        HttpContext context,
        CancellationToken cancellationToken = default);
}
