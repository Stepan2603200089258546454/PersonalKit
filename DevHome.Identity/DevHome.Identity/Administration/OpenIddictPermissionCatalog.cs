using DevHome.Identity.OpenIddict;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace DevHome.Identity.Administration;

/// <summary>
/// Один элемент справочника разрешений: значение OpenIddict и русская подпись.
/// </summary>
/// <param name="Value">Строковое значение, которое хранится в базе OpenIddict.</param>
/// <param name="Title">Подпись для интерфейса администратора.</param>
/// <param name="Hint">Короткое пояснение, зачем нужно разрешение.</param>
public sealed record PermissionOption(string Value, string Title, string? Hint = null);

/// <summary>
/// Справочник разрешений, требований и типов клиентов OpenIddict для админки.
/// </summary>
/// <remarks>
/// Значения берутся из констант OpenIddict, а не вписываются строками: при обновлении
/// пакета компилятор подскажет, если константа переименована. Подписи и подсказки —
/// единственное, что здесь задано вручную.
/// </remarks>
public static class OpenIddictPermissionCatalog
{
    /// <summary>Конечные точки, которые клиент вправе вызывать.</summary>
    public static IReadOnlyList<PermissionOption> Endpoints { get; } =
    [
        new(Permissions.Endpoints.Authorization, "Авторизация", "Вход пользователя через браузер (authorization code)."),
        new(Permissions.Endpoints.Token, "Токен", "Обмен кода, refresh-токена или данных клиента на токены."),
        new(Permissions.Endpoints.EndSession, "Завершение сеанса", "Выход из Identity по запросу клиента."),
        new(Permissions.Endpoints.DeviceAuthorization, "Код устройства", "Запрос кода для входа на устройстве без браузера."),
        new(Permissions.Endpoints.Introspection, "Интроспекция", "Проверка состояния токена на сервере."),
        new(Permissions.Endpoints.Revocation, "Отзыв токена", "Досрочный отзыв access- или refresh-токена.")
    ];

    /// <summary>Сценарии получения токена.</summary>
    public static IReadOnlyList<PermissionOption> GrantTypes { get; } =
    [
        new(Permissions.GrantTypes.AuthorizationCode, "Код авторизации", "Вход из браузера или настольного приложения."),
        new(Permissions.GrantTypes.RefreshToken, "Продление сеанса", "Получение нового access-токена без повторного входа."),
        new(Permissions.GrantTypes.ClientCredentials, "Учётные данные клиента", "Межсервисные вызовы без пользователя."),
        new(Permissions.GrantTypes.DeviceCode, "Код устройства", "Вход на устройстве, где нет браузера."),
        new(Permissions.GrantTypes.Password, "Пароль", "Передача логина и пароля напрямую (не рекомендуется).")
    ];

    /// <summary>Типы ответа на запрос авторизации.</summary>
    public static IReadOnlyList<PermissionOption> ResponseTypes { get; } =
    [
        new(Permissions.ResponseTypes.Code, "Код", "Стандартный ответ для authorization code flow."),
        new(Permissions.ResponseTypes.CodeIdToken, "Код и identity-токен", "Гибридный сценарий: код и данные пользователя сразу."),
        new(Permissions.ResponseTypes.CodeIdTokenToken, "Код, identity- и access-токен", "Гибридный сценарий с тремя значениями."),
        new(Permissions.ResponseTypes.CodeToken, "Код и access-токен", "Гибридный сценарий с кодом и токеном доступа."),
        new(Permissions.ResponseTypes.IdToken, "Identity-токен", "Только данные пользователя, без токена доступа."),
        new(Permissions.ResponseTypes.IdTokenToken, "Identity- и access-токен", "Implicit flow (устарел)."),
        new(Permissions.ResponseTypes.Token, "Access-токен", "Implicit flow (устарел)."),
        new(Permissions.ResponseTypes.None, "Без ответа", "Только проверка запроса, без выдачи токенов.")
    ];

    /// <summary>Области, которые разрешено запрашивать клиенту.</summary>
    public static IReadOnlyList<PermissionOption> Scopes { get; } =
    [
        new(Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OpenId, "openid", "Идентификатор пользователя (sub). Обязателен для OpenID Connect."),
        new(Permissions.Scopes.Profile, "profile", "Имя, username и другие данные профиля."),
        new(Permissions.Scopes.Email, "email", "Адрес электронной почты и признак подтверждения."),
        new(Permissions.Scopes.Address, "address", "Почтовый адрес из профиля."),
        new(Permissions.Scopes.Phone, "phone", "Номер телефона из профиля."),
        new(Permissions.Scopes.Roles, "roles", "Роли пользователя в токене."),
        new(Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OfflineAccess, "offline_access", "Право получить refresh-токен."),
        new(Permissions.Prefixes.Scope + DevHomeScopes.Api, "devhome.api", "Доступ к API приложений DevHome.")
    ];

    /// <summary>Требования, которые сервер предъявляет клиенту.</summary>
    public static IReadOnlyList<PermissionOption> Requirements { get; } =
    [
        new(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange, "PKCE", "Код авторизации без verifier бесполезен. Обязательно для публичных клиентов.")
    ];

    /// <summary>Типы клиентских приложений.</summary>
    public static IReadOnlyList<PermissionOption> ClientTypeOptions { get; } =
    [
        new(ClientTypes.Public, "Публичный", "Не может хранить секрет: SPA, мобильное или настольное приложение."),
        new(ClientTypes.Confidential, "Конфиденциальный", "Серверное приложение: дополнительно проверяется client_secret.")
    ];

    /// <summary>Типы согласия пользователя.</summary>
    public static IReadOnlyList<PermissionOption> ConsentTypeOptions { get; } =
    [
        new(ConsentTypes.Implicit, "Без экрана согласия", "Приложение своё: разрешения выдаются сразу."),
        new(ConsentTypes.Explicit, "Спрашивать пользователя", "Показывать экран согласия при первом входе."),
        new(ConsentTypes.External, "Внешнее согласие", "Решение принимает внешняя система."),
        new(ConsentTypes.Systematic, "Без согласия (системное)", "Доверенное приложение, согласие не требуется.")
    ];


    /// <summary>Подпись разрешения по его значению (если значение известно).</summary>
    public static string Describe(string value)
    {
        foreach (var group in new[] { Endpoints, GrantTypes, ResponseTypes, Scopes, Requirements })
        {
            var option = group.FirstOrDefault(item => string.Equals(item.Value, value, StringComparison.Ordinal));
            if (option is not null)
            {
                return option.Title;
            }
        }

        return value;
    }

    /// <summary>Подпись типа токена по значению, которое хранит OpenIddict.</summary>
    /// <remarks>
    /// OpenIddict хранит в колонке <c>Type</c> не короткое имя, а идентификатор типа
    /// (URN). Для стандартных типов используются константы пакета, для типов
    /// OpenIddict — их опубликованные значения.
    /// </remarks>
    public static string DescribeTokenType(string? type) => type switch
    {
        "access_token" or OpenIddictConstants.TokenTypeIdentifiers.AccessToken => "Access-токен",
        "refresh_token" or OpenIddictConstants.TokenTypeIdentifiers.RefreshToken => "Refresh-токен",
        "id_token" or OpenIddictConstants.TokenTypeIdentifiers.IdentityToken => "Identity-токен",
        "authorization_code" or "urn:openiddict:params:oauth:token-type:authorization_code" => "Код авторизации",
        "device_code" or "urn:openiddict:params:oauth:token-type:device_code" => "Код устройства",
        "user_code" or "urn:openiddict:params:oauth:token-type:user_code" => "Код пользователя",
        _ => type ?? "—"
    };

    /// <summary>Классы Bootstrap для значка состояния.</summary>
    public static string StatusBadgeClass(string? status) => status switch
    {
        Statuses.Valid => "text-bg-success",
        Statuses.Redeemed => "text-bg-info",
        Statuses.Revoked => "text-bg-secondary",
        Statuses.Rejected => "text-bg-danger",
        _ => "text-bg-warning"
    };

    /// <summary>Русская подпись состояния токена или авторизации.</summary>
    public static string DescribeStatus(string? status) => status switch
    {
        Statuses.Valid => "действителен",
        Statuses.Redeemed => "использован",
        Statuses.Revoked => "отозван",
        Statuses.Rejected => "отклонён",
        Statuses.Inactive => "неактивен",
        _ => status ?? "неизвестно"
    };
}
