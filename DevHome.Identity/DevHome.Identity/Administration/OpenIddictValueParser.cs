using System.Text.Json;

namespace DevHome.Identity.Administration;

/// <summary>
/// Разбор значений, которые OpenIddict хранит в базе в виде JSON.
/// </summary>
/// <remarks>
/// Список областей авторизации лежит в колонке <c>Scopes</c> как JSON-массив
/// (<c>["openid","profile"]</c>). В интерфейсе нужны отдельные значения,
/// поэтому строка разбирается один раз здесь, а не в каждой странице.
/// </remarks>
public static class OpenIddictValueParser
{
    /// <summary>Разбирает JSON-массив областей; при ошибке формат считается простым списком.</summary>
    public static string[] ParseScopes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(value) is { Length: > 0 } scopes ? scopes : [];
        }
        catch (JsonException)
        {
            return value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    /// <summary>Читаемое представление областей: <c>openid, profile</c>.</summary>
    public static string FormatScopes(string? value) => string.Join(", ", ParseScopes(value));
}
