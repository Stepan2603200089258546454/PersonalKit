using System.Text.RegularExpressions;

namespace Common.Security.OpenIddict.Server.Security;

/// <summary>
/// Разбирает user agent на браузер, операционную систему и тип устройства.
/// </summary>
/// <remarks>
/// Полноценный парсер user agent — большая библиотека с базой шаблонов. Здесь
/// сознательно используется небольшой набор правил: он покрывает Chrome, Edge,
/// Firefox, Safari, Яндекс.Браузер, Windows, macOS, Linux, Android и iOS.
/// Если строка не распознана, устройство всё равно учитывается, но называется
/// «Неизвестное устройство».
/// </remarks>
public sealed record UserAgentInfo(
    string? Browser,
    string? OperatingSystem,
    string? DeviceType,
    string DisplayName)
{
    private static readonly Regex AndroidVersion = new(@"Android\s+[\d.]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WindowsVersion = new(@"Windows NT\s+[\d.]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MacVersion = new(@"Mac OS X\s+[\d_]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ChromeVersion = new(@"Chrome/[\d.]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FirefoxVersion = new(@"Firefox/[\d.]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EdgeVersion = new(@"Edg/[\d.]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YandexVersion = new(@"YaBrowser/[\d.]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OperaVersion = new(@"OPR/[\d.]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Разбирает строку user agent.</summary>
    public static UserAgentInfo Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return new UserAgentInfo(null, null, null, "Неизвестное устройство");
        }

        var browser = DetectBrowser(userAgent);
        var operatingSystem = DetectOperatingSystem(userAgent);
        var deviceType = DetectDeviceType(userAgent);

        var parts = new[] { browser, operatingSystem }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        var displayName = parts.Length == 0
            ? "Неизвестное устройство"
            : string.Join(" · ", parts);

        if (!string.IsNullOrWhiteSpace(deviceType) && deviceType != "Компьютер")
        {
            displayName = $"{displayName} ({deviceType})";
        }

        return new UserAgentInfo(browser, operatingSystem, deviceType, displayName);
    }

    private static string? DetectBrowser(string userAgent)
    {
        if (userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase))
        {
            return WithVersion("Microsoft Edge", EdgeVersion.Match(userAgent).Value);
        }

        if (userAgent.Contains("YaBrowser/", StringComparison.OrdinalIgnoreCase))
        {
            return WithVersion("Яндекс.Браузер", YandexVersion.Match(userAgent).Value);
        }

        if (userAgent.Contains("OPR/", StringComparison.OrdinalIgnoreCase))
        {
            return WithVersion("Opera", OperaVersion.Match(userAgent).Value);
        }

        if (userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase))
        {
            return WithVersion("Firefox", FirefoxVersion.Match(userAgent).Value);
        }

        if (userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase))
        {
            return WithVersion("Chrome", ChromeVersion.Match(userAgent).Value);
        }

        if (userAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase)
            && userAgent.Contains("Version/", StringComparison.OrdinalIgnoreCase))
        {
            return "Safari";
        }

        return null;
    }

    private static string? DetectOperatingSystem(string userAgent)
    {
        if (userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase))
        {
            return WithVersion("Windows", WindowsVersion.Match(userAgent).Value);
        }

        if (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase))
        {
            return WithVersion("Android", AndroidVersion.Match(userAgent).Value);
        }

        if (userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPod", StringComparison.OrdinalIgnoreCase))
        {
            return "iOS";
        }

        if (userAgent.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase))
        {
            return WithVersion("macOS", MacVersion.Match(userAgent).Value);
        }

        if (userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase))
        {
            return "Linux";
        }

        return null;
    }

    private static string DetectDeviceType(string userAgent)
    {
        if (userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Tablet", StringComparison.OrdinalIgnoreCase))
        {
            return "Планшет";
        }

        if (userAgent.Contains("Mobi", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase))
        {
            return "Телефон";
        }

        if (userAgent.Contains("Bot", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains(" crawler", StringComparison.OrdinalIgnoreCase))
        {
            return "Робот";
        }

        return "Компьютер";
    }

    private static string WithVersion(string name, string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return name;
        }

        var slash = version.IndexOf('/');
        return slash >= 0 ? $"{name} {version[(slash + 1)..]}" : name;
    }
}
