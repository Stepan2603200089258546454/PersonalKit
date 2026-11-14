namespace DevHome.Identity.OpenIddict;

/// <summary>
/// Идентификаторы и адреса возврата клиентских приложений DevHome.
/// </summary>
/// <remarks>
/// ClientId — это «логин» приложения, он не секретный и виден в запросе на вход.
/// Адрес возврата (redirect URI) должен совпадать с зарегистрированным символ в символ,
/// иначе сервер отклонит запрос: так кража кода авторизации чужим приложением невозможна.
/// </remarks>
internal static class DevHomeClients
{
    /// <summary>Клиент Blazor Hybrid на MAUI. Публичный клиент: без секрета, только PKCE.</summary>
    public const string Maui = "devhome-maui";

    /// <summary>Клиент Blazor Hybrid на WPF. Публичный клиент: без секрета, только PKCE.</summary>
    public const string Wpf = "devhome-wpf";

    /// <summary>Клиент командной строки (device code flow). Публичный клиент.</summary>
    public const string Cli = "devhome-cli";

    /// <summary>Веб-клиент DevHome. Конфиденциальный клиент: у него есть секрет.</summary>
    public const string Web = "devhome-web";

    /// <summary>Сервисный клиент для межсервисных вызовов (client credentials).</summary>
    public const string Service = "devhome-service";

    /// <summary>
    /// Клиент API для проверки токенов через introspection. Сам API не входит
    /// в систему как пользователь: он только спрашивает Identity, действителен ли токен.
    /// </summary>
    public const string Api = "devhome-api";

    /// <summary>
    /// Адрес возврата MAUI-клиента. Используется 127.0.0.1, а не localhost:
    /// локальный HTTP-сервер слушает именно этот адрес, и строка должна совпадать
    /// с зарегистрированной в базе Identity.
    /// </summary>
    public static readonly Uri MauiRedirectUri = new("http://127.0.0.1:5180/callback");

    /// <summary>Адрес возврата WPF-клиента (второй порт, чтобы клиенты не мешали друг другу).</summary>
    public static readonly Uri WpfRedirectUri = new("http://127.0.0.1:5181/callback");
}
