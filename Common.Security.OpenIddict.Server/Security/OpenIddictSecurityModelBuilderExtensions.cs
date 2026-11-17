using Microsoft.EntityFrameworkCore;

namespace Common.Security.OpenIddict.Server.Security;

/// <summary>
/// Описание таблиц устройств, сеансов и журнала безопасности в EF Core.
/// </summary>
/// <remarks>
/// Вызовите <see cref="ConfigureOpenIddictSecurity"/> из <c>OnModelCreating</c>
/// вашего <c>DbContext</c>. Таблицы не имеют внешних ключей на таблицу пользователей:
/// библиотека не знает типа пользователя и не зависит от Identity. Связь с
/// пользователем — строковый <c>UserId</c> с индексом.
/// </remarks>
public static class OpenIddictSecurityModelBuilderExtensions
{
    /// <summary>Добавляет в модель таблицы безопасности OpenIddict.</summary>
    /// <param name="modelBuilder">Построитель модели EF Core.</param>
    public static ModelBuilder ConfigureOpenIddictSecurity(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<UserDevice>(entity =>
        {
            entity.ToTable("Security_UserDevices");
            entity.HasKey(device => device.Id);
            entity.Property(device => device.UserId).HasMaxLength(450).IsRequired();
            entity.Property(device => device.KeyHash).HasMaxLength(64).IsRequired();
            entity.Property(device => device.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(device => device.Browser).HasMaxLength(100);
            entity.Property(device => device.OperatingSystem).HasMaxLength(100);
            entity.Property(device => device.DeviceType).HasMaxLength(50);
            entity.Property(device => device.FirstIpAddress).HasMaxLength(64);
            entity.Property(device => device.LastIpAddress).HasMaxLength(64);
            entity.Property(device => device.LastLocation).HasMaxLength(200);
            entity.Property(device => device.UserAgent).HasMaxLength(1000);
            entity.Property(device => device.BlockedBy).HasMaxLength(450);
            entity.Property(device => device.BlockReason).HasMaxLength(500);
            entity.HasIndex(device => new { device.UserId, device.KeyHash }).IsUnique();
            entity.HasIndex(device => new { device.UserId, device.IsBlocked });
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("Security_UserSessions");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.UserId).HasMaxLength(450).IsRequired();
            entity.Property(session => session.ClientId).HasMaxLength(100).IsRequired();
            entity.Property(session => session.ApplicationName).HasMaxLength(200);
            entity.Property(session => session.RevokedBy).HasMaxLength(450);
            entity.Property(session => session.RevokeReason).HasMaxLength(500);
            entity.Property(session => session.IpAddress).HasMaxLength(64);
            entity.Property(session => session.Location).HasMaxLength(200);
            entity.Property(session => session.UserAgent).HasMaxLength(1000);
            entity.HasIndex(session => new { session.UserId, session.RevokedAt });
            entity.HasIndex(session => session.DeviceId);
            entity.HasOne(session => session.Device)
                .WithMany(device => device.Sessions)
                .HasForeignKey(session => session.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserSessionApplication>(entity =>
        {
            entity.ToTable("Security_SessionApplications");
            entity.HasKey(application => application.Id);
            entity.Property(application => application.ClientId).HasMaxLength(100).IsRequired();
            entity.Property(application => application.ApplicationName).HasMaxLength(200);
            entity.Property(application => application.Scopes).HasMaxLength(1000);
            entity.HasIndex(application => new { application.SessionId, application.ClientId }).IsUnique();
            entity.HasOne(application => application.Session)
                .WithMany(session => session.Applications)
                .HasForeignKey(application => application.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserLoginAudit>(entity =>
        {
            entity.ToTable("Security_LoginAudit");
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.UserId).HasMaxLength(450).IsRequired();
            entity.Property(audit => audit.EventType).HasMaxLength(100).IsRequired();
            entity.Property(audit => audit.DeviceName).HasMaxLength(200);
            entity.Property(audit => audit.ClientId).HasMaxLength(100);
            entity.Property(audit => audit.ApplicationName).HasMaxLength(200);
            entity.Property(audit => audit.IpAddress).HasMaxLength(64);
            entity.Property(audit => audit.Location).HasMaxLength(200);
            entity.Property(audit => audit.UserAgent).HasMaxLength(1000);
            entity.Property(audit => audit.Details).HasMaxLength(2000);
            entity.HasIndex(audit => new { audit.UserId, audit.OccurredAt });
            entity.HasIndex(audit => audit.SessionId);
            entity.HasIndex(audit => audit.OccurredAt);
        });

        modelBuilder.Entity<UserSecuritySetting>(entity =>
        {
            entity.ToTable("Security_UserSettings");
            entity.HasKey(setting => setting.UserId);
            entity.Property(setting => setting.UserId).HasMaxLength(450);
            entity.Property(setting => setting.DevicePolicy).HasConversion<string>().HasMaxLength(50);
        });

        return modelBuilder;
    }
}
