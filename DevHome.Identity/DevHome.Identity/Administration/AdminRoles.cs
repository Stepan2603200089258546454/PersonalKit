namespace DevHome.Identity.Administration;

/// <summary>
/// Имена ролей приложения.
/// </summary>
/// <remarks>
/// Роль — это набор прав, который попадает в cookie входа и в токены. Админка
/// защищена ролью <see cref="Administrator"/>: страницы помечены
/// <c>[Authorize(Roles = AdminRoles.Administrator)]</c>, а пункты меню — свойством
/// <c>Roles</c> атрибута <c>[MenuItem]</c>, поэтому обычный пользователь их не видит.
/// </remarks>
public static class AdminRoles
{
    /// <summary>Администратор: полный доступ к пользователям и настройкам OpenIddict.</summary>
    public const string Administrator = "Admin";

    /// <summary>Обычный пользователь приложения.</summary>
    public const string User = "User";
}
