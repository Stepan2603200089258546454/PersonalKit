using Common.UI.Menu;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Common.UI;

/// <summary>
/// Подключение библиотеки компонентов <c>Common.UI</c> к приложению.
/// </summary>
public static class CommonUiServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует меню навигации: настройки и справочник страниц.
    /// </summary>
    /// <param name="services">Коллекция сервисов приложения.</param>
    /// <param name="configure">Настройки приложения, адреса входа и сборки со страницами.</param>
    /// <example>
    /// <code>
    /// builder.Services.AddCommonUi(options =>
    /// {
    ///     options.ApplicationName = "DevHome";
    ///     options.LoginUrl = "login";
    ///     options.AddAssembly(typeof(Program).Assembly);
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddCommonUi(
        this IServiceCollection services,
        Action<NavigationMenuOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<NavigationMenuOptions>();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IMenuCatalog, MenuCatalog>();

        return services;
    }
}
