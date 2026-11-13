namespace Common.UI.Menu;

/// <summary>
/// Настройки страницы для меню навигации. Задаются атрибутом прямо на странице,
/// поэтому отдельный builder меню не нужен.
/// </summary>
/// <example>
/// <code>
/// @attribute [MenuItem("Главная", Icon = "bi bi-house-door", Section = "Разделы", Order = 10)]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MenuItemAttribute : Attribute
{
    /// <summary>
    /// Создаёт атрибут пункта меню.
    /// </summary>
    /// <param name="title">Название пункта в меню и заголовок страницы.</param>
    public MenuItemAttribute(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title;
    }

    /// <summary>Название пункта меню и заголовок страницы в верхней панели.</summary>
    public string Title { get; }

    /// <summary>Описание страницы: показывается в верхней панели рядом с заголовком.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Раздел меню (блок). Пункты с одинаковым разделом группируются вместе,
    /// а сам раздел выводится заголовком. <c>null</c> — пункт без заголовка.
    /// </summary>
    public string? Section { get; init; }

    /// <summary>Классы иконки Bootstrap Icons, например <c>bi bi-gear</c>.</summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Адрес пункта. Если не задан, берётся маршрут страницы из <c>@page</c>.
    /// Задаётся для внешних ссылок и страниц без компонента.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// Роли, которым виден пункт, через запятую (например, <c>Admin,Manager</c>).
    /// Пусто — пункт виден всем.
    /// </summary>
    public string? Roles { get; init; }

    /// <summary>
    /// Показывать пункт только вошедшим пользователям — без привязки к роли.
    /// </summary>
    public bool RequiresAuthorization { get; init; }

    /// <summary>Открывать страницу в новой вкладке.</summary>
    public bool OpenInNewTab { get; init; }

    /// <summary>Порядок пункта внутри раздела. Чем меньше число, тем выше пункт.</summary>
    public int Order { get; init; } = 100;

    /// <summary>Не показывать страницу в меню (маршрут при этом работает).</summary>
    public bool Hidden { get; init; }
}
