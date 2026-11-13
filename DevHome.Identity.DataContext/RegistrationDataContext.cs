using Common.Helper.DataBase;
using DevHome.Identity.DataContext.Context;
using DevHome.Identity.DataContext.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace DevHome.Identity.DataContext;

public static class RegistrationDataContext
{
    public static void AddIdentity(IServiceCollection services, IConfiguration configuration)
    {
        RegistrationPostgreSQL.AddDBSettings(services, configuration);
        RegistrationPostgreSQL.AddDBContext<DBContext>(services, options => options.UseOpenIddict());

        // OpenIddict core и ASP.NET Core Identity получают контекст из DI, поэтому
        // дополнительно регистрируем scoped-экземпляр поверх пула фабрик.
        services.AddScoped<DBContext>(provider =>
            provider.GetRequiredService<IDbContextFactory<DBContext>>().CreateDbContext());

        services.AddDatabaseDeveloperPageExceptionFilter();

        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.SignIn.RequireConfirmedAccount = true;
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        })
        .AddRoles<ApplicationRole>()
        .AddEntityFrameworkStores<DBContext>()
        .AddSignInManager()
        .AddDefaultTokenProviders();
    }
}
