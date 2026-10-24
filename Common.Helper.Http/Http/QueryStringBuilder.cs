using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Common.Helper.Http;

/// <summary>
/// Накапливает параметры строки запроса и вставляет их в адрес перед фрагментом.
/// Имена и значения кодируются по RFC 3986 (<c>Uri.EscapeDataString</c>);
/// коллекция раскрывается в несколько параметров с одним именем.
/// </summary>
internal sealed class QueryStringBuilder
{
    private readonly List<QueryParameter> _parameters = [];

    public int Count => _parameters.Count;

    /// <summary>
    /// Добавляет параметр со значением. Коллекция (кроме строки) раскрывается в несколько
    /// параметров с тем же именем; <c>null</c> даёт пустое значение.
    /// </summary>
    public void Add(string name, object? value)
    {
        if (value is null or string || value is not IEnumerable items)
        {
            _parameters.Add(new QueryParameter(name, QueryValueFormatter.Format(value)));
            return;
        }

        foreach (var item in items)
        {
            _parameters.Add(new QueryParameter(name, QueryValueFormatter.Format(item)));
        }
    }

    /// <summary>Добавляет параметр без значения (флаг вида <c>?debug</c>).</summary>
    public void AddFlag(string name) => _parameters.Add(new QueryParameter(name, null));

    /// <summary>Добавляет набор параметров, сохраняя порядок и повторы имён.</summary>
    public void AddRange(IEnumerable<KeyValuePair<string, string?>> parameters)
    {
        foreach (var parameter in parameters)
        {
            Add(parameter.Key, parameter.Value);
        }
    }

    /// <summary>
    /// Возвращает адрес с параметрами, вставленными перед фрагментом.
    /// Если параметров нет, адрес возвращается без изменений.
    /// </summary>
    public string AppendTo(string requestUri)
    {
        if (_parameters.Count == 0)
        {
            return requestUri;
        }

        // Фрагмент адреса должен остаться в конце: "api/items#section" + "?page=2"
        // превращается в "api/items?page=2#section".
        var fragmentIndex = requestUri.IndexOf('#');
        var path = fragmentIndex < 0 ? requestUri : requestUri[..fragmentIndex];
        var fragment = fragmentIndex < 0 ? string.Empty : requestUri[fragmentIndex..];

        var separator = path.EndsWith('?') || path.EndsWith('&')
            ? string.Empty
            : path.Contains('?')
                ? "&"
                : "?";

        var builder = new StringBuilder(path.Length + fragment.Length + (_parameters.Count * 16) + 1);
        builder.Append(path).Append(separator);

        for (var index = 0; index < _parameters.Count; index++)
        {
            if (index > 0)
            {
                builder.Append('&');
            }

            var parameter = _parameters[index];
            builder.Append(Uri.EscapeDataString(parameter.Name));

            if (parameter.Value is not null)
            {
                builder.Append('=').Append(Uri.EscapeDataString(parameter.Value));
            }
        }

        builder.Append(fragment);
        return builder.ToString();
    }
}
