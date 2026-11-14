namespace DevHome.Identity.Data;

/// <summary>
/// Состояние готовности базы данных. По нему docker-compose решает,
/// можно ли запускать DevHome и API: они не должны стартовать раньше,
/// чем Identity применит миграции и создаст начальные данные.
/// </summary>
/// <remarks>
/// Класс зарегистрирован как singleton и заполняется фоновым сервисом
/// <see cref="DatabaseInitializer"/>. Сам по себе он ничего не блокирует:
/// приложение начинает слушать порт сразу, но <c>/health</c> отвечает 503,
/// пока база не готова.
/// </remarks>
public sealed class DatabaseReadiness
{
    private volatile bool _isReady;
    private string? _lastError;

    /// <summary>База данных проверена, миграции и начальные данные применены.</summary>
    public bool IsReady => _isReady;

    /// <summary>Последняя ошибка подготовки базы; <c>null</c>, если ошибок не было.</summary>
    public string? LastError => _lastError;

    /// <summary>Отмечает базу готовой и очищает прошлую ошибку.</summary>
    public void MarkReady()
    {
        _lastError = null;
        _isReady = true;
    }

    /// <summary>Запоминает ошибку и оставляет базу неготовой.</summary>
    /// <param name="error">Текст ошибки для журнала и ответа <c>/health</c>.</param>
    public void MarkFailed(string error)
    {
        _lastError = error;
        _isReady = false;
    }
}
