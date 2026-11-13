using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace Common.UI.Menu;

/// <summary>
/// Собирает меню из <see cref="MenuItemAttribute"/> на страницах приложения.
/// </summary>
/// <remarks>
/// Сканирование выполняется один раз при первом обращении: атрибуты статичны,
/// поэтому повторный поиск по сборкам при каждой навигации не нужен.
/// </remarks>
public sealed class MenuCatalog : IMenuCatalog
{
    private readonly IReadOnlyList<MenuPage> _pages;
    private readonly IReadOnlyList<MenuSection> _sections;

    /// <summary>
    /// Создаёт справочник меню по зарегистрированным сборкам.
    /// </summary>
    /// <param name="options">Настройки меню с перечнем сборок.</param>
    public MenuCatalog(IOptions<NavigationMenuOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _pages = Discover(options.Value.Assemblies);
        _sections = BuildSections(_pages);
    }

    /// <inheritdoc />
    public IReadOnlyList<MenuSection> Sections => _sections;

    /// <inheritdoc />
    public MenuPage? FindPage(string? relativeUrl)
    {
        var normalizedUrl = MenuRoutes.Normalize(relativeUrl);

        MenuPage? best = null;
        var bestSegments = -1;

        foreach (var page in _pages)
        {
            if (page.RouteTemplate is null || !MenuRoutes.Matches(page.RouteTemplate, normalizedUrl))
            {
                continue;
            }

            var segments = MenuRoutes.Normalize(page.RouteTemplate).Count(c => c == '/');
            if (segments > bestSegments)
            {
                best = page;
                bestSegments = segments;
            }
        }

        return best ?? _pages.FirstOrDefault(page =>
            string.Equals(page.Url, normalizedUrl, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<MenuPage> Discover(IEnumerable<Assembly> assemblies)
    {
        var pages = new List<MenuPage>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in assemblies.Distinct())
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type.GetCustomAttribute<MenuItemAttribute>(inherit: false) is not { Hidden: false } attribute)
                {
                    continue;
                }

                var routes = type
                    .GetCustomAttributes<RouteAttribute>(inherit: false)
                    .Select(route => route.Template)
                    .Where(template => !string.IsNullOrWhiteSpace(template))
                    .ToArray();

                var url = attribute.Url ?? routes.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var normalizedUrl = MenuRoutes.Normalize(url);
                if (!seenUrls.Add(normalizedUrl))
                {
                    continue;
                }

                pages.Add(new MenuPage
                {
                    Title = attribute.Title,
                    Description = attribute.Description,
                    Url = normalizedUrl,
                    Icon = attribute.Icon,
                    Section = attribute.Section,
                    Roles = attribute.Roles,
                    RequiresAuthorization = attribute.RequiresAuthorization,
                    OpenInNewTab = attribute.OpenInNewTab,
                    Order = attribute.Order,
                    RouteTemplate = routes.FirstOrDefault()
                });
            }
        }

        return pages;
    }

    private static IReadOnlyList<MenuSection> BuildSections(IReadOnlyList<MenuPage> pages)
    {
        var sections = new List<MenuSection>();
        var items = new Dictionary<string, List<MenuPage>>(StringComparer.Ordinal);

        // Порядок обхода задаёт порядок разделов: сначала пункты с меньшим Order,
        // поэтому раздел появляется там, где впервые встречается его пункт.
        foreach (var page in pages.OrderBy(page => page.Order).ThenBy(page => page.Title, StringComparer.Ordinal))
        {
            var key = page.Section ?? string.Empty;

            if (!items.TryGetValue(key, out var list))
            {
                list = [];
                items[key] = list;
                sections.Add(new MenuSection
                {
                    Title = string.IsNullOrWhiteSpace(key) ? null : key,
                    Items = list
                });
            }

            list.Add(page);
        }

        return sections;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }
}
