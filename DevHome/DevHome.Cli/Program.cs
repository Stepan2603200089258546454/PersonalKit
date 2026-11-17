using System.Diagnostics;
using System.Text.Json;
using Common.Security.OpenIddict.Client;

namespace DevHome.Cli;

/// <summary>
/// Пример входа в DevHome.Identity из командной строки по device code flow.
/// </summary>
/// <remarks>
/// Работа с протоколом вынесена в <see cref="OpenIddictDeviceCodeClient"/>.
/// Здесь остаётся только консольный интерфейс: показать код, открыть браузер,
/// дождаться подтверждения и напечатать профиль.
/// </remarks>
internal static class Program
{
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

        var client = new OpenIddictDeviceCodeClient(new OpenIddictDeviceCodeClientOptions
        {
            Authority = settings.Authority,
            ClientId = settings.ClientId,
            Scope = settings.Scope
        });

        var prompt = await client.RequestAsync();
        if (prompt is null)
        {
            Console.Error.WriteLine("Не удалось получить код устройства.");
            return 1;
        }

        Console.WriteLine($"Откройте страницу: {prompt.VerificationUri}");
        Console.WriteLine($"Введите код: {prompt.UserCode}");
        Console.WriteLine();

        TryOpenBrowser(prompt.VerificationUriComplete ?? prompt.VerificationUri);

        var token = await WaitForTokenAsync(client, prompt);
        if (token is null)
        {
            return 1;
        }

        Console.WriteLine("Вход выполнен.");
        Console.WriteLine();

        await PrintUserInfoAsync(client, token.AccessToken);

        if (!string.IsNullOrEmpty(token.RefreshToken))
        {
            Console.WriteLine();
            Console.WriteLine("Обновление access-токена по refresh token...");

            var refreshed = await client.RefreshAsync(token.RefreshToken);
            if (refreshed is not null)
            {
                Console.WriteLine($"Новый access-токен получен, действует {refreshed.ExpiresIn} с.");
            }
        }

        return 0;
    }

    private static async Task<DeviceCodeToken?> WaitForTokenAsync(
        OpenIddictDeviceCodeClient client,
        DeviceCodePrompt prompt)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(prompt.ExpiresIn);
        var interval = TimeSpan.FromSeconds(Math.Max(prompt.Interval, 1));

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(interval);

            var result = await client.PollAsync(prompt);
            switch (result.Status)
            {
                case DeviceCodePollStatus.Success:
                    return result.Token;

                case DeviceCodePollStatus.Pending:
                    Console.Write('.');
                    continue;

                case DeviceCodePollStatus.SlowDown:
                    interval += TimeSpan.FromSeconds(5);
                    continue;

                case DeviceCodePollStatus.AccessDenied:
                case DeviceCodePollStatus.Expired:
                case DeviceCodePollStatus.Error:
                    Console.Error.WriteLine();
                    Console.Error.WriteLine(result.Error);
                    return null;

                default:
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Неизвестный ответ сервера.");
                    return null;
            }
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine("Время ожидания подтверждения истекло.");
        return null;
    }

    private static async Task PrintUserInfoAsync(OpenIddictDeviceCodeClient client, string accessToken)
    {
        using var document = await client.GetUserInfoAsync(accessToken);
        if (document is null)
        {
            Console.Error.WriteLine("Не удалось получить профиль.");
            return;
        }

        Console.WriteLine("Профиль:");
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var value = property.Value.ValueKind == JsonValueKind.Array
                ? string.Join(", ", property.Value.EnumerateArray().Select(item => item.ToString()))
                : property.Value.ToString();

            Console.WriteLine($"  {property.Name}: {value}");
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
            // В средах без графической оболочки открыть браузер не получится —
            // пользователь откроет ссылку вручную.
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
