using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevHome.Cli;

/// <summary>
/// Пример входа в DevHome.Identity из командной строки по device code flow.
/// </summary>
internal static class Program
{
    private static readonly HttpClient HttpClient = new();

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var settings = CliSettings.Load();
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            settings = settings with { Authority = args[0] };
        }

        try
        {
            return await RunAsync(settings);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Ошибка: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunAsync(CliSettings settings)
    {
        Console.WriteLine("Вход в DevHome через DevHome.Identity (device code flow)");
        Console.WriteLine($"Сервер: {settings.Authority}");
        Console.WriteLine();

        var device = await RequestDeviceCodeAsync(settings);
        if (device is null)
        {
            return 1;
        }

        Console.WriteLine($"Откройте страницу: {device.VerificationUri}");
        Console.WriteLine($"Введите код: {device.UserCode}");
        Console.WriteLine();

        TryOpenBrowser(device.VerificationUriComplete ?? device.VerificationUri);

        var token = await PollForTokenAsync(settings, device);
        if (token is null)
        {
            return 1;
        }

        Console.WriteLine("Вход выполнен.");
        Console.WriteLine();

        await PrintUserInfoAsync(settings, token.AccessToken);

        if (!string.IsNullOrEmpty(token.RefreshToken))
        {
            Console.WriteLine();
            Console.WriteLine("Обновление access-токена по refresh token...");
            var refreshed = await RefreshAsync(settings, token.RefreshToken);
            if (refreshed is not null)
            {
                Console.WriteLine($"Новый access-токен получен, действует {refreshed.ExpiresIn} с.");
            }
        }

        return 0;
    }

    private static async Task<DeviceCodeResponse?> RequestDeviceCodeAsync(CliSettings settings)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId,
            ["scope"] = settings.Scope
        });

        using var response = await HttpClient.PostAsync(
            new Uri(new Uri(settings.Authority), "connect/deviceauthorization"),
            content);

        var payload = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"Не удалось получить код устройства ({(int)response.StatusCode}): {payload}");
            return null;
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        return new DeviceCodeResponse(
            root.GetProperty("device_code").GetString()!,
            root.GetProperty("user_code").GetString()!,
            root.GetProperty("verification_uri").GetString()!,
            root.TryGetProperty("verification_uri_complete", out var complete) ? complete.GetString() : null,
            root.GetProperty("expires_in").GetInt32(),
            root.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 5);
    }

    private static async Task<TokenResponse?> PollForTokenAsync(CliSettings settings, DeviceCodeResponse device)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(device.ExpiresIn);
        var interval = TimeSpan.FromSeconds(Math.Max(device.Interval, 1));

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(interval);

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = device.DeviceCode,
                ["client_id"] = settings.ClientId
            });

            using var response = await HttpClient.PostAsync(
                new Uri(new Uri(settings.Authority), "connect/token"),
                content);

            var payload = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                var token = JsonNode.Parse(payload)!;
                return new TokenResponse(
                    token["access_token"]!.GetValue<string>(),
                    token["refresh_token"]?.GetValue<string>(),
                    token["expires_in"]?.GetValue<int>() ?? 0);
            }

            var error = ReadError(payload);
            switch (error)
            {
                case "authorization_pending":
                    Console.Write('.');
                    continue;

                case "slow_down":
                    interval += TimeSpan.FromSeconds(5);
                    continue;

                case "access_denied":
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Пользователь отклонил вход.");
                    return null;

                case "expired_token":
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Срок действия кода устройства истёк.");
                    return null;

                default:
                    Console.Error.WriteLine();
                    Console.Error.WriteLine($"Ошибка токена: {error}");
                    return null;
            }
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine("Время ожидания подтверждения истекло.");
        return null;
    }

    private static async Task<TokenResponse?> RefreshAsync(CliSettings settings, string refreshToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = settings.ClientId
        });

        using var response = await HttpClient.PostAsync(
            new Uri(new Uri(settings.Authority), "connect/token"),
            content);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var token = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        return new TokenResponse(
            token["access_token"]!.GetValue<string>(),
            token["refresh_token"]?.GetValue<string>(),
            token["expires_in"]?.GetValue<int>() ?? 0);
    }

    private static async Task PrintUserInfoAsync(CliSettings settings, string accessToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(settings.Authority), "connect/userinfo"));

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await HttpClient.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"Не удалось получить профиль ({(int)response.StatusCode}): {payload}");
            return;
        }

        using var document = JsonDocument.Parse(payload);
        Console.WriteLine("Профиль:");
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var value = property.Value.ValueKind == JsonValueKind.Array
                ? string.Join(", ", property.Value.EnumerateArray().Select(item => item.ToString()))
                : property.Value.ToString();

            Console.WriteLine($"  {property.Name}: {value}");
        }
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

    private static void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // В средах без графической оболочки открыть браузер не получится — пользователь откроет ссылку вручную.
        }
    }
}

/// <summary>Параметры клиента из appsettings.json.</summary>
internal sealed record CliSettings(string Authority, string ClientId, string Scope)
{
    public static CliSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var json = File.Exists(path)
            ? File.ReadAllText(path)
            : "{}";

        using var document = JsonDocument.Parse(json);
        var section = document.RootElement.TryGetProperty("Authentication", out var auth) &&
                      auth.TryGetProperty("DevHome", out var devHome)
            ? devHome
            : default;

        return new CliSettings(
            ReadString(section, "Authority") ?? "http://127.0.0.1:5201/",
            ReadString(section, "ClientId") ?? "devhome-cli",
            ReadString(section, "Scope") ?? "openid profile email offline_access devhome.api");
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value)
            ? value.GetString()
            : null;
}

internal sealed record DeviceCodeResponse(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string? VerificationUriComplete,
    int ExpiresIn,
    int Interval);

internal sealed record TokenResponse(string AccessToken, string? RefreshToken, int ExpiresIn);
