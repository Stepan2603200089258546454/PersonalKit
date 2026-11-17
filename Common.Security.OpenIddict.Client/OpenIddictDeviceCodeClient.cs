using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Common.Security.OpenIddict.Client;

/// <summary>Настройки клиента device code flow.</summary>
public sealed class OpenIddictDeviceCodeClientOptions
{
    /// <summary>Адрес Identity.</summary>
    public string Authority { get; set; } = "http://127.0.0.1:5201/";

    /// <summary>ClientId публичного клиента, например <c>devhome-cli</c>.</summary>
    public string ClientId { get; set; } = "devhome-cli";

    /// <summary>Области через пробел.</summary>
    public string Scope { get; set; } = "openid profile email offline_access devhome.api";

    /// <summary>HttpClient для запросов. Если не задан, создаётся новый.</summary>
    public HttpClient? HttpClient { get; set; }
}

/// <summary>Код устройства и адрес, который нужно открыть пользователю.</summary>
public sealed record DeviceCodePrompt(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string? VerificationUriComplete,
    int ExpiresIn,
    int Interval);

/// <summary>Токены, полученные после подтверждения кода устройства.</summary>
public sealed record DeviceCodeToken(string AccessToken, string? RefreshToken, int ExpiresIn);

/// <summary>Состояние одного опроса token endpoint.</summary>
public enum DeviceCodePollStatus
{
    /// <summary>Токен получен.</summary>
    Success = 0,

    /// <summary>Пользователь ещё не подтвердил код.</summary>
    Pending = 1,

    /// <summary>Нужно увеличить интервал опроса.</summary>
    SlowDown = 2,

    /// <summary>Пользователь отклонил вход.</summary>
    AccessDenied = 3,

    /// <summary>Срок кода истёк.</summary>
    Expired = 4,

    /// <summary>Другая ошибка.</summary>
    Error = 5
}

/// <summary>Результат опроса token endpoint.</summary>
public sealed record DeviceCodePollResult(
    DeviceCodePollStatus Status,
    DeviceCodeToken? Token = null,
    string? Error = null);

/// <summary>
/// Клиент device code flow: устройства без браузера (CLI, консоль).
/// </summary>
/// <remarks>
/// Приложение показывает пользователю <see cref="DeviceCodePrompt.UserCode"/> и
/// <see cref="DeviceCodePrompt.VerificationUri"/>, затем периодически вызывает
/// <see cref="PollAsync"/>. Пока пользователь не подтвердил код, сервер отвечает
/// <c>authorization_pending</c>; после подтверждения возвращает токены.
/// </remarks>
public sealed class OpenIddictDeviceCodeClient
{
    private readonly OpenIddictDeviceCodeClientOptions _options;
    private readonly HttpClient _httpClient;

    /// <summary>Создаёт клиент по настройкам.</summary>
    public OpenIddictDeviceCodeClient(OpenIddictDeviceCodeClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientId);

        _options = options;
        _httpClient = options.HttpClient ?? new HttpClient();
    }

    /// <summary>Запрашивает код устройства.</summary>
    public async Task<DeviceCodePrompt?> RequestAsync(CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["scope"] = _options.Scope
        });

        using var response = await _httpClient.PostAsync(
            new Uri(new Uri(_options.Authority), "connect/deviceauthorization"),
            content,
            cancellationToken);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Не удалось получить код устройства ({(int)response.StatusCode}): {payload}",
                null,
                response.StatusCode);
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        return new DeviceCodePrompt(
            root.GetProperty("device_code").GetString()!,
            root.GetProperty("user_code").GetString()!,
            root.GetProperty("verification_uri").GetString()!,
            root.TryGetProperty("verification_uri_complete", out var complete) ? complete.GetString() : null,
            root.GetProperty("expires_in").GetInt32(),
            root.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 5);
    }

    /// <summary>Один опрос token endpoint.</summary>
    public async Task<DeviceCodePollResult> PollAsync(
        DeviceCodePrompt prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            ["device_code"] = prompt.DeviceCode,
            ["client_id"] = _options.ClientId
        });

        using var response = await _httpClient.PostAsync(
            new Uri(new Uri(_options.Authority), "connect/token"),
            content,
            cancellationToken);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return new DeviceCodePollResult(
                DeviceCodePollStatus.Success,
                ParseToken(payload));
        }

        var error = ReadError(payload);
        return error switch
        {
            "authorization_pending" => new DeviceCodePollResult(DeviceCodePollStatus.Pending),
            "slow_down" => new DeviceCodePollResult(DeviceCodePollStatus.SlowDown),
            "access_denied" => new DeviceCodePollResult(DeviceCodePollStatus.AccessDenied, Error: "Пользователь отклонил вход."),
            "expired_token" => new DeviceCodePollResult(DeviceCodePollStatus.Expired, Error: "Срок действия кода устройства истёк."),
            _ => new DeviceCodePollResult(DeviceCodePollStatus.Error, Error: $"Ошибка токена: {error}")
        };
    }

    /// <summary>Обновляет access-токен по refresh-токену.</summary>
    public async Task<DeviceCodeToken?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = _options.ClientId
        });

        using var response = await _httpClient.PostAsync(
            new Uri(new Uri(_options.Authority), "connect/token"),
            content,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return ParseToken(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>Запрашивает профиль по access-токену.</summary>
    public async Task<JsonDocument?> GetUserInfoAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(_options.Authority), "connect/userinfo"));

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static DeviceCodeToken ParseToken(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        return new DeviceCodeToken(
            root.GetProperty("access_token").GetString()!,
            root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
            root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 0);
    }

    private static string ReadError(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("error", out var error)
                ? error.GetString() ?? "unknown_error"
                : "unknown_error";
        }
        catch (JsonException)
        {
            return "unknown_error";
        }
    }
}
