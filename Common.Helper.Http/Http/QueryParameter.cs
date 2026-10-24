namespace Common.Helper.Http;

/// <summary>
/// Один параметр строки запроса.
/// <see cref="Value"/> = <c>null</c> означает параметр без значения (флаг вида <c>?debug</c>);
/// пустая строка означает <c>?name=</c>.
/// </summary>
internal readonly record struct QueryParameter(string Name, string? Value);
