using DevHome.Identity.DataContext;
using DevHome.Identity.OpenIddict;

namespace DevHome.Identity.Data;

/// <summary>
/// Применяет миграции и создаёт начальные данные в фоне, чтобы сервер
/// успевал начать слушать порт и браузер открывался сразу при отладке.
/// </summary>
public sealed class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<DatabaseInitializer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Database:AutoMigrate", true))
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();

            await MigrationDataContext.AutoMigrateAsync(scope);
            await OpenIddictSeeder.SeedAsync(scope.ServiceProvider, configuration, environment);

            logger.LogInformation("База данных DevHome.Identity готова.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Не удалось подготовить базу данных DevHome.Identity.");
        }
    }
}
