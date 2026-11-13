using Common.Helper.DataBase.Settings;
using Common.Helper.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Common.Helper.DataBase
{
    public static class RegistrationPostgreSQL
    {
        public static void AddDBSettings(IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptionsValidate<PostgreSettings>(configuration, PostgreSettings.ConfigSectionName);
        }

        /// <summary>
        /// Добавить и сконфигурировать контекс БД
        /// </summary>
        public static void AddDBContext<TContext>(IServiceCollection services) where TContext : DbContext
        {
            PostgreSettings dbOptions = services
                .BuildServiceProvider()
                .GetRequiredService<IOptions<PostgreSettings>>()
                .Value;

            services.AddPooledDbContextFactory<TContext>(optionsAction: (provider, options) =>
            {
                PostgreSettings opt = provider.GetRequiredService<IOptions<PostgreSettings>>().Value;

                if (opt.DefaultConnection is null)
                    throw new ArgumentNullException(nameof(opt.DefaultConnection), "Строка подключения к PostgreSQL не найдена");
                if (opt.Version is null)
                    throw new ArgumentNullException(nameof(opt.Version), "Версия PostgreSQL не найдена");

                options.UseNpgsql(opt.DefaultConnection, npgsqlOptions =>
                {
                    npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                    npgsqlOptions.SetPostgresVersion(Version.Parse(opt.Version));
                })
                    .LogTo(Console.WriteLine, LogLevel.Information)
                    .EnableSensitiveDataLogging()
                    .EnableDetailedErrors();
            }, poolSize: dbOptions?.PoolSize ?? 1024);
        }
    }
}
