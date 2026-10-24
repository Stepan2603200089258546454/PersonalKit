using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Common.Helper.Http.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Common.Helper.Http.Tests;

/// <summary>
/// Проверяет полную горячую перезагрузку параметров из настоящего appsettings.json:
/// файл перечитывается файловым провайдером конфигурации, параметры клиента применяются без перезапуска.
/// </summary>
public sealed class AppSettingsReloadTests
{
    private const string Section = "HttpClients:FileApi";

    [Fact]
    public async Task JsonFileReload_UpdatesClientParameters()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            "reload-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "appsettings.json");

        try
        {
            await File.WriteAllTextAsync(file, Json("https://first.test/", "one"), Encoding.UTF8);

            var configuration = new ConfigurationBuilder()
                .AddJsonFile(file, optional: false, reloadOnChange: true)
                .Build();

            var inner = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
            var services = new ServiceCollection();
            services.AddLogging();
            services
                .AddResilientHttpClient<ITestClient, TestClient>(configuration, Section)
                .ConfigurePrimaryHttpMessageHandler(() => inner);

            using var provider = services.BuildServiceProvider();
            var client = provider.GetRequiredService<ITestClient>();
            var monitor = provider.GetRequiredService<IOptionsMonitor<ResilientHttpClientOptions>>();
            var clientName = typeof(ITestClient).FullName!;

            using var first = await client.GetAsync("api/items");
            Assert.Equal("https://first.test/api/items", inner.Requests[0].RequestUri!.ToString());

            await File.WriteAllTextAsync(file, Json("https://second.test/v2/", "two"), Encoding.UTF8);

            await WaitUntilAsync(
                () => monitor.Get(clientName).BaseAddress == "https://second.test/v2/",
                TimeSpan.FromSeconds(10));

            using var second = await client.GetAsync("api/items");

            Assert.Equal("https://second.test/v2/api/items", inner.Requests[1].RequestUri!.ToString());
            Assert.Equal(["two"], inner.Requests[1].Headers["X-Env"]);

            (configuration as IDisposable)?.Dispose();
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Каталог может быть занят наблюдателем файлов — это не влияет на результат теста.
            }
        }
    }

    private static string Json(string baseAddress, string environment) =>
        $$"""
        {
          "HttpClients": {
            "FileApi": {
              "BaseAddress": "{{baseAddress}}",
              "Timeout": "00:00:15",
              "DefaultHeaders": {
                "X-Env": "{{environment}}"
              },
              "Resilience": {
                "Retry": {
                  "MaxRetryAttempts": 1,
                  "Delay": "00:00:00.001",
                  "MaxDelay": "00:00:00.010"
                },
                "AttemptTimeout": {
                  "Enabled": false
                }
              }
            }
          }
        }
        """;

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Параметры не перезагрузились за {timeout}.");
    }
}
