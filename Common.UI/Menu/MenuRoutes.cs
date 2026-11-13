namespace Common.UI.Menu;

/// <summary>
/// Работа с адресами и шаблонами маршрутов меню.
/// </summary>
public static class MenuRoutes
{
    /// <summary>
    /// Приводит адрес к виду, в котором хранятся пункты меню:
    /// без ведущего слеша, без параметров запроса и якоря.
    /// </summary>
    /// <param name="url">Абсолютный или относительный адрес.</param>
    public static string Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var value = url;

        var absolute = Uri.TryCreate(value, UriKind.Absolute, out var uri);
        if (absolute && uri is not null)
        {
            value = uri.AbsolutePath;
        }

        var queryIndex = value.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
        {
            value = value[..queryIndex];
        }

        return value.Trim().Trim('/');
    }

    /// <summary>
    /// Проверяет, подходит ли шаблон маршрута под текущий адрес.
    /// </summary>
    /// <param name="template">Шаблон маршрута, например <c>weather/{city}</c>.</param>
    /// <param name="url">Текущий относительный адрес.</param>
    /// <remarks>
    /// Параметр <c>{name}</c> совпадает с любым непустым сегментом,
    /// <c>{*rest}</c> и <c>{**rest}</c> — с остатком адреса.
    /// </remarks>
    public static bool Matches(string template, string? url)
    {
        var normalizedTemplate = Normalize(template);
        var normalizedUrl = Normalize(url);

        if (normalizedTemplate.Length == 0)
        {
            return normalizedUrl.Length == 0;
        }

        var templateSegments = normalizedTemplate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var urlSegments = normalizedUrl.Length == 0
            ? []
            : normalizedUrl.Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < templateSegments.Length; index++)
        {
            var templateSegment = templateSegments[index];

            if (templateSegment.StartsWith("{*", StringComparison.Ordinal) ||
                templateSegment.StartsWith("{**", StringComparison.Ordinal))
            {
                return true;
            }

            if (index >= urlSegments.Length)
            {
                return false;
            }

            if (templateSegment.StartsWith('{') && templateSegment.EndsWith('}'))
            {
                continue;
            }

            if (!string.Equals(templateSegment, urlSegments[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return templateSegments.Length == urlSegments.Length;
    }
}
