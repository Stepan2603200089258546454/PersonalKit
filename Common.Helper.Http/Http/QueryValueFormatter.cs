using System;
using System.Globalization;

namespace Common.Helper.Http;

/// <summary>
/// Приводит значение параметра строки запроса к тексту в инвариантной культуре.
/// Даты и время форматируются по ISO 8601, чтобы адрес был однозначным и сортируемым.
/// </summary>
internal static class QueryValueFormatter
{
    public static string Format(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        bool flag => flag ? "true" : "false",
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture),
        DateTimeOffset offset => offset.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
