using Common.Helper.DataBase.Settings;
using DevHome.Identity.DataContext.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DevHome.Identity.DataContext;

public static class MigrationDataContext
{
    /// <summary>
    /// (расширение) Проверить и применить миграцию
    /// </summary>
    [Obsolete("Рекомендуется использовать асинхронную версию")]
    public static void AutoMigrate(IHost host)
    {
        AutoMigrateAsync(host).GetAwaiter().GetResult();
    }
    /// <summary>
    /// (расширение) Проверить и применить миграцию
    /// </summary>
    public static async Task AutoMigrateAsync(IHost host)
    {
        // Миграции EF Core
        using IServiceScope scope = host.Services.CreateScope();
        await AutoMigrateAsync(scope);
    }
    /// <summary>
    /// Проверить и применить миграцию
    /// </summary>
    public static async Task AutoMigrateAsync(IServiceScope scope)
    {
        PostgreSettings dbOptions = scope.ServiceProvider.GetRequiredService<IOptions<PostgreSettings>>().Value;

        IDbContextFactory<DBContext> dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DBContext>>();
        await using DBContext dbContext = await dbContextFactory.CreateDbContextAsync();
        if ((await dbContext.Database.GetPendingMigrationsAsync())?.Any() == true) //проверяем нужны ли миграции
            dbContext.Database.Migrate(); //Пытаемся актуализировать и принять миграции
    }
}
