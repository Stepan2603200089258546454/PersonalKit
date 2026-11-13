using Common.Helper.DataBase;
using DevHome.Identity.DataContext.Context;
using DevHome.Identity.DataContext.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace DevHome.Identity.DataContext;

public static class RegistrationDataContext
{
    public static void AddIdentity(IServiceCollection services, IConfiguration configuration)
    {
        RegistrationPostgreSQL.AddDBSettings(services, configuration);
        RegistrationPostgreSQL.AddDBContext<DBContext>(services);

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
