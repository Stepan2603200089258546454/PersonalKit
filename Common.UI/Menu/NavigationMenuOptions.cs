using System.Reflection;

namespace Common.UI.Menu;

/// <summary>
/// Настройки меню навигации. Регистрируются один раз при запуске приложения
/// через <c>AddCommonUi</c>, страницы описывают себя атрибутами.
/// </summary>
public sealed class NavigationMenuOptions
{
    /// <summary>Название приложения в шапке боковой панели.</summary>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>Классы иконки приложения Bootstrap Icons.</summary>
    public string ApplicationIcon { get; set; } = "bi bi-grid-1x2-fill";

    /// <summary>Адрес главной страницы (на него ведёт логотип).</summary>
    public string HomeUrl { get; set; } = string.Empty;

    /// <summary>Адрес страницы входа. Пусто — кнопка «Войти» не показывается.</summary>
    public string? LoginUrl { get; set; }

    /// <summary>Адрес страницы регистрации. Пусто — кнопка не показывается.</summary>
    public string? RegisterUrl { get; set; }

    /// <summary>Адрес страницы профиля. Пусто — пункт меню профиля не показывается.</summary>
    public string? ProfileUrl { get; set; }

    /// <summary>Адрес выхода. Пусто — кнопка выхода не показывается.</summary>
    public string? LogoutUrl { get; set; }

    /// <summary>
    /// Выход выполняется POST-формой. Нужно для Identity, где выход защищён antiforgery.
    /// <c>false</c> — выход обычной ссылкой (например, обработчик <c>/logout</c>).
    /// </summary>
    public bool LogoutUsePost { get; set; } = true;

    /// <summary>Добавлять в форму выхода antiforgery-токен.</summary>
    public bool UseAntiforgeryToken { get; set; } = true;

    /// <summary>Показывать форму поиска в верхней панели.</summary>
    public bool ShowSearch { get; set; }

    /// <summary>Адрес формы поиска.</summary>
    public string SearchUrl { get; set; } = "search";

    /// <summary>Имя параметра поискового запроса.</summary>
    public string SearchParameter { get; set; } = "q";

    /// <summary>Показывать переключатель темы в верхней панели.</summary>
    public bool ShowThemeSwitcher { get; set; } = true;

    /// <summary>
    /// Сборки, в которых ищутся страницы с <see cref="MenuItemAttribute"/>.
    /// </summary>
    public IList<Assembly> Assemblies { get; } = new List<Assembly>();

    /// <summary>
    /// Добавляет сборку для поиска страниц меню.
    /// </summary>
    /// <param name="assembly">Сборка с компонентами-страницами.</param>
    public NavigationMenuOptions AddAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (!Assemblies.Contains(assembly))
        {
            Assemblies.Add(assembly);
        }

        return this;
    }
}
