namespace Common.UI.Menu;

/// <summary>
/// Раздел (блок) меню: необязательный заголовок и пункты под ним.
/// </summary>
public sealed class MenuSection
{
    /// <summary>Заголовок раздела. <c>null</c> — пункты без заголовка.</summary>
    public string? Title { get; init; }

    /// <summary>Пункты раздела в порядке отображения.</summary>
    public required IReadOnlyList<MenuPage> Items { get; init; }
}
