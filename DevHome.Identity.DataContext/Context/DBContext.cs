using Common.Security.OpenIddict.Server.Security;
using DevHome.Identity.DataContext.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DevHome.Identity.DataContext.Context
{
    public class DBContext(DbContextOptions<DBContext> options) : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
    {
        /// <summary>Устройства, с которых пользователи входили в приложение.</summary>
        public DbSet<UserDevice> UserDevices => Set<UserDevice>();

        /// <summary>Сеансы пользователей.</summary>
        public DbSet<UserSession> UserSessions => Set<UserSession>();

        /// <summary>Приложения, в которые входили в рамках сеанса.</summary>
        public DbSet<UserSessionApplication> SessionApplications => Set<UserSessionApplication>();

        /// <summary>Журнал событий безопасности.</summary>
        public DbSet<UserLoginAudit> LoginAudit => Set<UserLoginAudit>();

        /// <summary>Настройки безопасности пользователей.</summary>
        public DbSet<UserSecuritySetting> UserSecuritySettings => Set<UserSecuritySetting>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Таблицы библиотеки устройств и сеансов. Внешних ключей на
            // AspNetUsers нет: библиотека не зависит от класса пользователя.
            builder.ConfigureOpenIddictSecurity();
        }
    }
}
