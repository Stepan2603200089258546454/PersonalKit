namespace DevHome.Identity.Administration;

/// <summary>
/// Общие подписи и адреса страниц администрирования.
/// </summary>
/// <remarks>
/// Значения используются и в разметке, и в перенаправлениях после сохранения,
/// поэтому собраны в одном месте: при переименовании маршрута правится одна строка.
/// </remarks>
public static class AdminNavigation
{
    /// <summary>Раздел меню, в котором собраны страницы администрирования.</summary>
    public const string Section = "Администрирование";

    /// <summary>Сводная страница администрирования.</summary>
    public const string Dashboard = "/admin";

    /// <summary>Список пользователей.</summary>
    public const string Users = "/admin/users";

    /// <summary>Создание пользователя.</summary>
    public const string NewUser = "/admin/users/new";

    /// <summary>Клиентские приложения OpenIddict.</summary>
    public const string Clients = "/admin/clients";

    /// <summary>Создание клиента OpenIddict.</summary>
    public const string NewClient = "/admin/clients/new";

    /// <summary>Области OpenIddict.</summary>
    public const string Scopes = "/admin/scopes";

    /// <summary>Все устройства и сеансы пользователей.</summary>
    public const string Sessions = "/admin/sessions";

    /// <summary>Авторизации и токены OpenIddict.</summary>
    public const string Tokens = "/admin/tokens";

    /// <summary>Адрес страницы пользователя.</summary>
    public static string User(string? id) => $"{Users}/{id}";

    /// <summary>Адрес страницы клиента.</summary>
    public static string Client(string? id) => $"{Clients}/{id}";

    /// <summary>Адрес страницы области.</summary>
    public static string Scope(string? id) => $"{Scopes}/{id}";
}
