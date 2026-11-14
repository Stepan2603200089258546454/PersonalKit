using DevHome.Identity.DataContext;
using DevHome.Identity.OpenIddict;

namespace DevHome.Identity.Data;

/// <summary>
/// Применяет миграции и создаёт начальные данные в фоне, чтобы сервер
/// успевал начать слушать порт и браузер открывался сразу при отладке.
/// </summary>
/// <remarks>
/// В Docker база может стартовать позже приложения, поэтому попытки повторяются.
/// Пока подготовка не завершена, <see cref="DatabaseReadiness"/> оставляет
/// <c>/health</c> в состоянии 503, и docker-compose не запускает зависимые сервисы.
/// </remarks>
public sealed class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    DatabaseReadiness readiness,
    ILogger<DatabaseInitializer> logger) : BackgroundService
{
    private const int MaxAttempts = 12;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Database:AutoMigrate", true))
        {
            // Миграции применяются вручную: считаем, что база уже готова.
            readiness.MarkReady();
            return;
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();

                await MigrationDataContext.AutoMigrateAsync(scope);
                await OpenIddictSeeder.SeedAsync(scope.ServiceProvider, configuration, environment);

                readiness.MarkReady();
                logger.LogInformation("База данных DevHome.Identity готова.");
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                readiness.MarkFailed(exception.Message);

                if (attempt == MaxAttempts)
                {
                    logger.LogError(
                        exception,
                        "Не удалось подготовить базу данных DevHome.Identity за {Attempts} попыток.",
                        MaxAttempts);
                    return;
                }

                logger.LogWarning(
                    exception,
                    "База данных DevHome.Identity пока недоступна (попытка {Attempt} из {Attempts}). Повтор через {Delay} с.",
                    attempt,
                    MaxAttempts,
                    RetryDelay.TotalSeconds);

                try
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
