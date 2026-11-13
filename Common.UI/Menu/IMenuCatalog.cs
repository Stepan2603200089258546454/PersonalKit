namespace Common.UI.Menu;

/// <summary>
/// Справочник пунктов меню, собранный из атрибутов на страницах.
/// </summary>
public interface IMenuCatalog
{
    /// <summary>Разделы меню в порядке отображения.</summary>
    IReadOnlyList<MenuSection> Sections { get; }

    /// <summary>
    /// Находит страницу по текущему адресу, чтобы показать её название и описание
    /// в верхней панели.
    /// </summary>
    /// <param name="relativeUrl">Текущий относительный адрес без параметров запроса.</param>
    MenuPage? FindPage(string? relativeUrl);
}
