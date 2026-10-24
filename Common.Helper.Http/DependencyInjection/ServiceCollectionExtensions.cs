using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Common.Helper.Http;

/// <summary>
/// Регистрация типизированных HTTP-клиентов с устойчивостью и горячей перезагрузкой параметров.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует типизированный HTTP-клиент <typeparamref name="TClient"/> с реализацией
    /// <typeparamref name="TImplementation"/>.
    /// Параметры читаются из секции <paramref name="sectionName"/> конфигурации и перезагружаются
    /// при её изменении (нужен провайдер с поддержкой reload, например appsettings.json).
    /// </summary>
    /// <param name="services">Коллекция сервисов.</param>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <param name="sectionName">Путь к секции параметров клиента, например <c>HttpClients:TestApi</c>.</param>
    /// <param name="configure">Необязательные значения по умолчанию; значения из конфигурации имеют приоритет.</param>
    /// <returns>Построитель HTTP-клиента для дополнительной настройки.</returns>
    public static IHttpClientBuilder AddResilientHttpClient<TClient, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName,
        Action<ResilientHttpClientOptions>? configure = null)
        where TClient : class
        where TImplementation : class, TClient
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var clientName = typeof(TClient).FullName ?? typeof(TClient).Name;
        RegisterCore(services, clientName, configuration, sectionName, configure);

        return services
            .AddHttpClient<TClient, TImplementation>(ConfigureClient)
            .AddResilientHandler(clientName);
    }

    /// <summary>
    /// Регистрирует именованный HTTP-клиент с устойчивостью и горячей перезагрузкой параметров.
    /// </summary>
    /// <param name="services">Коллекция сервисов.</param>
    /// <param name="clientName">Имя клиента.</param>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <param name="sectionName">Путь к секции параметров клиента.</param>
    /// <param name="configure">Необязательные значения по умолчанию; значения из конфигурации имеют приоритет.</param>
    /// <returns>Построитель HTTP-клиента для дополнительной настройки.</returns>
    public static IHttpClientBuilder AddResilientHttpClient(
        this IServiceCollection services,
        string clientName,
        IConfiguration configuration,
        string sectionName,
        Action<ResilientHttpClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        RegisterCore(services, clientName, configuration, sectionName, configure);

        return services
            .AddHttpClient(clientName, ConfigureClient)
            .AddResilientHandler(clientName);
    }

    private static void ConfigureClient(HttpClient client)
    {
        // Таймаут и базовый адрес применяет обработчик: только так они могут меняться
        // без перезапуска приложения (HttpClient фиксирует их навсегда).
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.BaseAddress = ResilientHttpMessageHandler.DynamicBaseAddress;
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(ResilientHttpClientOptions))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(ResilienceOptions))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(RetryOptions))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(CircuitBreakerOptions))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(TimeoutOptions))]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "ConfigurationBinder связывает простые POCO опций; их свойства и конструкторы сохранены через DynamicDependency.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Связывание опций выполняется рефлексией по сохранённым свойствам простых POCO без генерации кода.")]
    private static void RegisterCore(
        IServiceCollection services,
        string clientName,
        IConfiguration configuration,
        string sectionName,
        Action<ResilientHttpClientOptions>? configure)
    {
        var optionsBuilder = services.AddOptions<ResilientHttpClientOptions>(clientName);

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        optionsBuilder.Bind(configuration.GetSection(sectionName));

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ResilientHttpClientOptions>, ResilientHttpClientOptionsValidator>());
        services.TryAddSingleton<ResiliencePipelineCache>();
    }

    private static IHttpClientBuilder AddResilientHandler(this IHttpClientBuilder builder, string clientName)
    {
        return builder.AddHttpMessageHandler(services => new ResilientHttpMessageHandler(
            clientName,
            services.GetRequiredService<IOptionsMonitor<ResilientHttpClientOptions>>(),
            services.GetRequiredService<ResiliencePipelineCache>(),
            services.GetService<ILoggerFactory>()));
    }
}
