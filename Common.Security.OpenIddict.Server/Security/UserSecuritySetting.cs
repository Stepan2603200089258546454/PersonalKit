namespace Common.Security.OpenIddict.Server.Security;

/// <summary>
/// Пользовательские настройки безопасности.
/// </summary>
/// <remarks>
/// Хранятся отдельно от <c>ApplicationUser</c>, чтобы библиотека не зависела
/// от конкретного класса пользователя и могла использоваться в других приложениях.
/// Запись создаётся при первом изменении политики; до этого действует
/// <see cref="DeviceLoginPolicy.MultipleDevices"/>.
/// </remarks>
public sealed class UserSecuritySetting
{
    /// <summary>Идентификатор пользователя.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Политика одновременного входа с устройств.</summary>
    public DeviceLoginPolicy DevicePolicy { get; set; } = DeviceLoginPolicy.MultipleDevices;

    /// <summary>Когда настройка изменена.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
