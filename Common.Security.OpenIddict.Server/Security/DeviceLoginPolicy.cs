namespace Common.Security.OpenIddict.Server.Security;

/// <summary>
/// Политика одновременного входа с нескольких устройств.
/// </summary>
public enum DeviceLoginPolicy
{
    /// <summary>Разрешить вход с любого числа устройств.</summary>
    MultipleDevices = 0,

    /// <summary>
    /// Разрешить только одно активное устройство: новый вход автоматически
    /// завершает все предыдущие сеансы пользователя.
    /// </summary>
    SingleDeviceNewestWins = 1,

    /// <summary>
    /// Разрешить только одно активное устройство: новый вход возможен после
    /// того, как пользователь явно завершит предыдущий сеанс (или его завершит
    /// администратор).
    /// </summary>
    SingleDeviceRequiresOldSessionEnd = 2
}
