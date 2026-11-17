using Common.Security.OpenIddict.Server.Security;

namespace DevHome.Identity.Data;

/// <summary>
/// Раз в сутки удаляет старые записи журнала безопасности.
/// </summary>
/// <remarks>
/// Журнал входов нужен для расследования инцидентов, но не должен расти бесконечно.
/// Срок хранения задаётся <c>Security:Sessions:AuditRetention</c> (по умолчанию 180 дней).
/// </remarks>
public sealed class SecurityCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<SecurityCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Первый запуск не сразу: при старте приложение занято миграциями и сидами.
        if (!await DelayAsync(TimeSpan.FromMinutes(1), stoppingToken))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var security = scope.ServiceProvider.GetRequiredService<IUserSecurityService>();
                var removed = await security.CleanupAuditAsync(stoppingToken);

                if (removed > 0)
                {
                    logger.LogInformation(
                        "Удалено старых записей журнала безопасности: {Count}.",
                        removed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Не удалось очистить старый журнал безопасности.");
            }

            if (!await DelayAsync(Interval, stoppingToken))
            {
                return;
            }
        }
    }

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
