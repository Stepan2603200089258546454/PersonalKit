namespace Common.UI.Menu;

/// <summary>
/// Пункт меню, собранный из <see cref="MenuItemAttribute"/> на странице.
/// </summary>
public sealed class MenuPage
{
    /// <summary>Название пункта меню.</summary>
    public required string Title { get; init; }

    /// <summary>Описание страницы для верхней панели.</summary>
    public string? Description { get; init; }

    /// <summary>Относительный адрес без ведущего слеша, например <c>weather</c>.</summary>
    public required string Url { get; init; }

    /// <summary>Классы иконки Bootstrap Icons.</summary>
    public string? Icon { get; init; }

    /// <summary>Раздел (блок) меню.</summary>
    public string? Section { get; init; }

    /// <summary>Роли, которым виден пункт, через запятую.</summary>
    public string? Roles { get; init; }

    /// <summary>Пункт виден только вошедшим пользователям.</summary>
    public bool RequiresAuthorization { get; init; }

    /// <summary>Открывать в новой вкладке.</summary>
    public bool OpenInNewTab { get; init; }

    /// <summary>Порядок внутри раздела.</summary>
    public int Order { get; init; }

    /// <summary>Шаблон маршрута страницы, если пункт ведёт на компонент.</summary>
    public string? RouteTemplate { get; init; }

    /// <summary>
    /// Признак того, что пункт является текущей страницей.
    /// </summary>
    /// <param name="currentUrl">Текущий относительный адрес без параметров запроса.</param>
    public bool IsCurrent(string currentUrl) =>
        RouteTemplate is not null
            ? MenuRoutes.Matches(RouteTemplate, currentUrl)
            : MenuRoutes.Normalize(Url) == MenuRoutes.Normalize(currentUrl);
}
