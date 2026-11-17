namespace Common.Security.OpenIddict.Client.Web;

/// <summary>
/// Подключает проверку access-токенов через introspection Identity.
/// </summary>
/// <remarks>
/// API не хранит ключи подписи и не читает базу Identity. Он отправляет токен
/// в <c>/connect/introspect</c> с учётными данными клиента <c>devhome-api</c>
/// и получает ответ «действителен / недействителен». Поэтому отзыв сеанса или
/// токена в Identity сразу закрывает доступ к API.
/// </remarks>
public static class OpenIddictApiValidationExtensions
{
    /// <summary>Добавляет схему аутентификации OpenIddict Validation.</summary>
    public static IServiceCollection AddOpenIddictApiValidation(
        this IServiceCollection services,
        OpenIddictApiValidationOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientSecret);

        services.AddAuthentication(authentication =>
        {
            authentication.DefaultScheme =
                global::OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        });

        services.AddOpenIddict()
            .AddValidation(validation =>
            {
                validation.SetIssuer(new Uri(options.Issuer, UriKind.Absolute));

                // Introspection вместо локальной проверки подписи: API не знает
                // ключей Identity и не должен иметь к ним доступ.
                validation.UseIntrospection()
                    .SetClientId(options.ClientId)
                    .SetClientSecret(options.ClientSecret);

                validation.UseSystemNetHttp();
                validation.UseAspNetCore();
            });

        return services;
    }
}
