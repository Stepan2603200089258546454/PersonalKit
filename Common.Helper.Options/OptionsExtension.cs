using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Helper.Options;

/// <summary>
/// Расширения для регистрации параметров приложения с проверкой по атрибутам данных.
/// </summary>
public static class OptionsExtension
{
    /// <summary>
    /// Привязывает параметры <typeparamref name="TOptions"/> к разделу конфигурации
    /// и включает проверку по атрибутам данных при старте приложения.
    /// </summary>
    /// <typeparam name="TOptions">Тип параметров.</typeparam>
    /// <param name="services">Коллекция сервисов.</param>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <param name="section">Имя раздела конфигурации.</param>
    public static IServiceCollection AddOptionsValidate<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string section)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);

        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
