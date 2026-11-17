using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Common.Security.OpenIddict.Server.Security;

/// <summary>
/// Реализация хранения устройств, сеансов, настроек и журнала безопасности.
/// </summary>
/// <remarks>
/// Сервис работает в scope запроса и использует тот же <typeparamref name="TContext"/>,
/// что и приложение. Это важно: Identity и OpenIddict уже используют этот контекст,
/// поэтому запись сеанса и токенов не требует отдельного подключения к базе.
/// </remarks>
public sealed class UserSecurityService<TContext>(
    TContext dbContext,
    ITokenRevocationService tokenRevocationService,
    IOptions<UserSecurityOptions> options,
    ILogger<UserSecurityService<TContext>> logger) : IUserSecurityService
    where TContext : DbContext
{
    private readonly UserSecurityOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<UserSecuritySettingsDto> GetSettingsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var setting = await dbContext.Set<UserSecuritySetting>()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        return new UserSecuritySettingsDto(
            setting?.DevicePolicy ?? DeviceLoginPolicy.MultipleDevices,
            setting?.UpdatedAt ?? DateTimeOffset.MinValue);
    }

    /// <inheritdoc />
    public async Task SetPolicyAsync(
        string userId,
        DeviceLoginPolicy policy,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var setting = await dbContext.Set<UserSecuritySetting>()
            .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        if (setting is null)
        {
            setting = new UserSecuritySetting { UserId = userId };
            dbContext.Set<UserSecuritySetting>().Add(setting);
        }

        setting.DevicePolicy = policy;
        setting.UpdatedAt = DateTimeOffset.UtcNow;

        AddAudit(
            userId,
            UserSecurityEventTypes.PolicyChanged,
            details: $"Новая политика: {DescribePolicy(policy)}.",
            actorUserId: actorUserId);

        // Политика «одно устройство» должна действовать сразу, а не только
        // со следующего входа. Оставляем самый свежий сеанс, остальные завершаем.
        var revokedClients = new HashSet<string>(StringComparer.Ordinal);
        if (policy != DeviceLoginPolicy.MultipleDevices)
        {
            var activeSessions = await LoadActiveSessionsAsync(userId, cancellationToken);
            var newest = activeSessions
                .OrderByDescending(session => session.LastSeenAt)
                .FirstOrDefault();

            foreach (var session in activeSessions.Where(session => session.Id != newest?.Id))
            {
                RevokeSessionInternal(
                    session,
                    actorUserId,
                    "Завершено при смене политики на одно устройство.",
                    revokedClients);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (revokedClients.Count > 0)
        {
            await RevokeTokensAsync(userId, revokedClients, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<SignInPolicyDecision> EvaluateSignInAsync(
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var device = await dbContext.Set<UserDevice>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == deviceId && item.UserId == userId,
                cancellationToken);

        if (device is null)
        {
            return new SignInPolicyDecision(
                SignInPolicyStatus.DeviceBlocked,
                "Устройство не найдено. Попробуйте войти заново.",
                []);
        }

        if (device.IsBlocked)
        {
            return new SignInPolicyDecision(
                SignInPolicyStatus.DeviceBlocked,
                "Это устройство заблокировано. Вход с него запрещён. " +
                "Разблокировать устройство может пользователь в разделе «Безопасность» или администратор.",
                []);
        }

        var settings = await GetSettingsAsync(userId, cancellationToken);
        if (settings.DevicePolicy == DeviceLoginPolicy.MultipleDevices)
        {
            return Allowed();
        }

        var active = await GetActiveSessionsAsync(userId, cancellationToken);
        var otherSessions = active.Where(session => session.DeviceId != deviceId).ToList();

        if (settings.DevicePolicy == DeviceLoginPolicy.SingleDeviceNewestWins)
        {
            // Новый вход разрешён: старые сеансы завершит StartSessionAsync.
            return Allowed();
        }

        if (otherSessions.Count == 0)
        {
            return Allowed();
        }

        return new SignInPolicyDecision(
            SignInPolicyStatus.RequiresOldSessionEnd,
            BuildConflictMessage(otherSessions),
            otherSessions);
    }

    /// <inheritdoc />
    public async Task<SessionStartResult> StartSessionAsync(
        string userId,
        Guid deviceId,
        string clientId,
        string? applicationName,
        RequestContextInfo request,
        string? authenticationMethod = null,
        CancellationToken cancellationToken = default)
    {
        var decision = await EvaluateSignInAsync(userId, deviceId, cancellationToken);
        if (!decision.IsAllowed)
        {
            AddAudit(
                userId,
                UserSecurityEventTypes.SignInRejected,
                sessionId: null,
                deviceId: deviceId,
                deviceName: decision.ConflictingSessions.FirstOrDefault()?.DeviceName,
                clientId: clientId,
                applicationName: applicationName,
                request: request,
                details: decision.Message);

            await dbContext.SaveChangesAsync(cancellationToken);
            return new SessionStartResult(decision.Status, null, decision.Message, decision.ConflictingSessions);
        }

        var device = await dbContext.Set<UserDevice>()
            .FirstAsync(item => item.Id == deviceId && item.UserId == userId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var revokedClients = new HashSet<string>(StringComparer.Ordinal);

        if (await GetSettingsAsync(userId, cancellationToken) is { DevicePolicy: DeviceLoginPolicy.SingleDeviceNewestWins })
        {
            var activeSessions = await LoadActiveSessionsAsync(userId, cancellationToken);
            foreach (var other in activeSessions.Where(session => session.DeviceId != deviceId))
            {
                RevokeSessionInternal(
                    other,
                    userId,
                    "Завершено новым входом по политике «новое устройство побеждает».",
                    revokedClients);
            }
        }

        var session = new UserSession
        {
            UserId = userId,
            DeviceId = device.Id,
            ClientId = string.IsNullOrWhiteSpace(clientId) ? _options.IdentityClientId : clientId,
            ApplicationName = applicationName ?? _options.IdentityApplicationName,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now.Add(_options.SessionLifetime),
            IpAddress = request.IpAddress,
            Location = request.Location,
            UserAgent = request.UserAgent
        };

        dbContext.Set<UserSession>().Add(session);
        AddAudit(
            userId,
            UserSecurityEventTypes.SignIn,
            session.Id,
            device.Id,
            device.DisplayName,
            session.ClientId,
            session.ApplicationName,
            request,
            $"Вход с устройства «{device.DisplayName}». Способ: {DescribeAuthenticationMethod(authenticationMethod)}.");

        await dbContext.SaveChangesAsync(cancellationToken);

        if (revokedClients.Count > 0)
        {
            await RevokeTokensAsync(userId, revokedClients, cancellationToken);
        }

        return new SessionStartResult(
            SignInPolicyStatus.Allowed,
            session.Id,
            null,
            []);
    }

    /// <inheritdoc />
    public async Task<SessionValidationResult> ValidateSessionAsync(
        string userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await dbContext.Set<UserSession>()
            .AsNoTracking()
            .Include(item => item.Device)
            .FirstOrDefaultAsync(
                item => item.Id == sessionId && item.UserId == userId,
                cancellationToken);

        if (session is null)
        {
            return new SessionValidationResult(false, "Сеанс не найден.");
        }

        if (session.RevokedAt is not null)
        {
            return new SessionValidationResult(false, session.RevokeReason ?? "Сеанс завершён.");
        }

        if (session.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            return new SessionValidationResult(false, "Срок сеанса истёк.");
        }

        if (session.Device?.IsBlocked == true)
        {
            return new SessionValidationResult(false, "Устройство заблокировано.");
        }

        return new SessionValidationResult(true, null);
    }

    /// <inheritdoc />
    public async Task TouchSessionAsync(
        string userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await dbContext.Set<UserSession>()
            .FirstOrDefaultAsync(
                item => item.Id == sessionId && item.UserId == userId,
                cancellationToken);

        if (session is null || !session.IsActive)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - session.LastSeenAt < _options.TouchInterval)
        {
            return;
        }

        session.LastSeenAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionSummary>> GetActiveSessionsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var sessions = await dbContext.Set<UserSession>()
            .AsNoTracking()
            .Include(session => session.Device)
            .Include(session => session.Applications)
            .Where(session => session.UserId == userId
                              && session.RevokedAt == null
                              && (session.ExpiresAt == null || session.ExpiresAt > now))
            .OrderByDescending(session => session.LastSeenAt)
            .ToListAsync(cancellationToken);

        return sessions.Select(ToSummary).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionSummary>> GetSessionHistoryAsync(
        string userId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var sessions = await dbContext.Set<UserSession>()
            .AsNoTracking()
            .Include(session => session.Device)
            .Include(session => session.Applications)
            .Where(session => session.UserId == userId)
            .OrderByDescending(session => session.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);

        return sessions.Select(ToSummary).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceSummary>> GetDevicesAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var devices = await dbContext.Set<UserDevice>()
            .AsNoTracking()
            .Where(device => device.UserId == userId)
            .OrderByDescending(device => device.LastSeenAt)
            .Select(device => new
            {
                Device = device,
                ActiveSessionCount = device.Sessions.Count(session =>
                    session.RevokedAt == null && (session.ExpiresAt == null || session.ExpiresAt > now))
            })
            .ToListAsync(cancellationToken);

        return devices
            .Select(item => new DeviceSummary(
                item.Device.Id,
                item.Device.UserId,
                item.Device.DisplayName,
                item.Device.Browser,
                item.Device.OperatingSystem,
                item.Device.DeviceType,
                item.Device.FirstIpAddress,
                item.Device.LastIpAddress,
                item.Device.LastLocation,
                item.Device.CreatedAt,
                item.Device.LastSeenAt,
                item.Device.IsBlocked,
                item.Device.BlockedAt,
                item.Device.BlockedBy,
                item.Device.BlockReason,
                item.ActiveSessionCount))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoginAuditSummary>> GetAuditAsync(
        string userId,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Set<UserLoginAudit>()
            .AsNoTracking()
            .Where(audit => audit.UserId == userId)
            .OrderByDescending(audit => audit.OccurredAt)
            .Take(Math.Clamp(take, 1, 500))
            .Select(audit => new LoginAuditSummary(
                audit.Id,
                audit.UserId,
                audit.EventType,
                audit.SessionId,
                audit.DeviceId,
                audit.DeviceName,
                audit.ClientId,
                audit.ApplicationName,
                audit.IpAddress,
                audit.Location,
                audit.UserAgent,
                audit.Details,
                audit.OccurredAt))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionSummary>> GetAllActiveSessionsAsync(
        int take = 200,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var sessions = await dbContext.Set<UserSession>()
            .AsNoTracking()
            .Include(session => session.Device)
            .Include(session => session.Applications)
            .Where(session => session.RevokedAt == null
                              && (session.ExpiresAt == null || session.ExpiresAt > now))
            .OrderByDescending(session => session.LastSeenAt)
            .Take(Math.Clamp(take, 1, 1000))
            .ToListAsync(cancellationToken);

        return sessions.Select(ToSummary).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoginAuditSummary>> GetAllAuditAsync(
        int take = 200,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Set<UserLoginAudit>()
            .AsNoTracking()
            .OrderByDescending(audit => audit.OccurredAt)
            .Take(Math.Clamp(take, 1, 1000))
            .Select(audit => new LoginAuditSummary(
                audit.Id,
                audit.UserId,
                audit.EventType,
                audit.SessionId,
                audit.DeviceId,
                audit.DeviceName,
                audit.ClientId,
                audit.ApplicationName,
                audit.IpAddress,
                audit.Location,
                audit.UserAgent,
                audit.Details,
                audit.OccurredAt))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RecordApplicationAsync(
        string userId,
        Guid sessionId,
        string clientId,
        string? applicationName,
        IEnumerable<string> scopes,
        RequestContextInfo request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        var session = await dbContext.Set<UserSession>()
            .Include(item => item.Device)
            .FirstOrDefaultAsync(
                item => item.Id == sessionId && item.UserId == userId,
                cancellationToken);

        if (session is null || !session.IsActive)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var scopeText = string.Join(' ', scopes.Where(scope => !string.IsNullOrWhiteSpace(scope)));

        // Не используем навигацию session.Applications: при параллельных запросах
        // (например, браузер одновременно запрашивает вход и ресурсы) коллекция
        // может содержать устаревшую строку. Явный запрос и Add дают EF однозначное
        // состояние Added/Modified.
        var application = await dbContext.Set<UserSessionApplication>()
            .FirstOrDefaultAsync(
                item => item.SessionId == sessionId && item.ClientId == clientId,
                cancellationToken);

        var isNewApplication = application is null;
        if (application is null)
        {
            application = new UserSessionApplication
            {
                SessionId = session.Id,
                ClientId = clientId,
                ApplicationName = applicationName,
                Scopes = scopeText,
                FirstSeenAt = now,
                LastSeenAt = now
            };

            dbContext.Set<UserSessionApplication>().Add(application);
        }
        else
        {
            application.ApplicationName = applicationName ?? application.ApplicationName;
            application.Scopes = scopeText;
            application.LastSeenAt = now;
            application.RevokedAt = null;
        }

        session.LastSeenAt = now;
        AddAudit(
            userId,
            UserSecurityEventTypes.ApplicationAuthorized,
            session.Id,
            session.DeviceId,
            session.Device?.DisplayName,
            clientId,
            applicationName,
            request,
            scopeText.Length == 0 ? null : $"Области: {scopeText}.");

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (isNewApplication)
        {
            // Два параллельных запроса (например, две вкладки) могли одновременно
            // добавить одно приложение. Уникальный индекс не даст создать дубль;
            // повторяем как обновление существующей строки.
            dbContext.Entry(application).State = EntityState.Detached;
            var concurrent = await dbContext.Set<UserSessionApplication>()
                .FirstOrDefaultAsync(
                    item => item.SessionId == sessionId && item.ClientId == clientId,
                    cancellationToken);

            if (concurrent is null)
            {
                throw;
            }

            concurrent.ApplicationName = applicationName ?? concurrent.ApplicationName;
            concurrent.Scopes = scopeText;
            concurrent.LastSeenAt = now;
            concurrent.RevokedAt = null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task RevokeSessionAsync(
        string userId,
        Guid sessionId,
        string actorUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var session = await dbContext.Set<UserSession>()
            .Include(item => item.Device)
            .Include(item => item.Applications)
            .FirstOrDefaultAsync(
                item => item.Id == sessionId && item.UserId == userId,
                cancellationToken);

        if (session is null || session.RevokedAt is not null)
        {
            return;
        }

        var clients = new HashSet<string>(StringComparer.Ordinal);
        RevokeSessionInternal(session, actorUserId, reason, clients);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (clients.Count > 0)
        {
            await RevokeTokensAsync(userId, clients, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task RevokeAllSessionsAsync(
        string userId,
        string actorUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var sessions = await LoadActiveSessionsAsync(userId, cancellationToken);
        var clients = new HashSet<string>(StringComparer.Ordinal);

        foreach (var session in sessions)
        {
            RevokeSessionInternal(session, actorUserId, reason, clients);
        }

        if (sessions.Count > 0)
        {
            AddAudit(
                userId,
                UserSecurityEventTypes.AllSessionsRevoked,
                details: $"Завершено сеансов: {sessions.Count}. Причина: {reason}",
                actorUserId: actorUserId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Для «завершить всё» надёжнее отозвать все токены пользователя,
        // даже если активных сеансов уже нет: access-токен мог остаться живым.
        await tokenRevocationService.RevokeAllAsync(userId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task BlockDeviceAsync(
        string userId,
        Guid deviceId,
        string actorUserId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var device = await dbContext.Set<UserDevice>()
            .FirstOrDefaultAsync(
                item => item.Id == deviceId && item.UserId == userId,
                cancellationToken);

        if (device is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        device.IsBlocked = true;
        device.BlockedAt = now;
        device.BlockedBy = actorUserId;
        device.BlockReason = string.IsNullOrWhiteSpace(reason) ? "Заблокировано пользователем." : reason;

        var sessions = await LoadActiveSessionsAsync(userId, cancellationToken);
        var deviceSessions = sessions.Where(session => session.DeviceId == deviceId).ToList();
        var clients = new HashSet<string>(StringComparer.Ordinal);

        foreach (var session in deviceSessions)
        {
            RevokeSessionInternal(session, actorUserId, device.BlockReason, clients);
        }

        AddAudit(
            userId,
            UserSecurityEventTypes.DeviceBlocked,
            deviceId: device.Id,
            deviceName: device.DisplayName,
            details: $"Устройство заблокировано. Завершено сеансов: {deviceSessions.Count}. {device.BlockReason}",
            actorUserId: actorUserId);

        await dbContext.SaveChangesAsync(cancellationToken);

        if (clients.Count > 0)
        {
            await RevokeTokensAsync(userId, clients, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task UnblockDeviceAsync(
        string userId,
        Guid deviceId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var device = await dbContext.Set<UserDevice>()
            .FirstOrDefaultAsync(
                item => item.Id == deviceId && item.UserId == userId,
                cancellationToken);

        if (device is null)
        {
            return;
        }

        device.IsBlocked = false;
        device.BlockedAt = null;
        device.BlockedBy = null;
        device.BlockReason = null;

        AddAudit(
            userId,
            UserSecurityEventTypes.DeviceUnblocked,
            deviceId: device.Id,
            deviceName: device.DisplayName,
            details: "Устройство разблокировано. Старые сеансы не восстанавливаются.",
            actorUserId: actorUserId);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteUserDataAsync(string userId, CancellationToken cancellationToken = default)
    {
        var sessions = dbContext.Set<UserSession>().Where(session => session.UserId == userId);
        var sessionIds = await sessions.Select(session => session.Id).ToListAsync(cancellationToken);

        await dbContext.Set<UserSessionApplication>()
            .Where(application => sessionIds.Contains(application.SessionId))
            .ExecuteDeleteAsync(cancellationToken);

        await sessions.ExecuteDeleteAsync(cancellationToken);
        await dbContext.Set<UserDevice>()
            .Where(device => device.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.Set<UserSecuritySetting>()
            .Where(setting => setting.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.Set<UserLoginAudit>()
            .Where(audit => audit.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CleanupAuditAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - _options.AuditRetention;
        return await dbContext.Set<UserLoginAudit>()
            .Where(audit => audit.OccurredAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<List<UserSession>> LoadActiveSessionsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return await dbContext.Set<UserSession>()
            .Include(session => session.Device)
            .Include(session => session.Applications)
            .Where(session => session.UserId == userId
                              && session.RevokedAt == null
                              && (session.ExpiresAt == null || session.ExpiresAt > now))
            .ToListAsync(cancellationToken);
    }

    private void RevokeSessionInternal(
        UserSession session,
        string actorUserId,
        string reason,
        HashSet<string> clients)
    {
        if (session.RevokedAt is not null)
        {
            return;
        }

        session.RevokedAt = DateTimeOffset.UtcNow;
        session.RevokedBy = actorUserId;
        session.RevokeReason = reason;

        foreach (var application in session.Applications)
        {
            application.RevokedAt ??= session.RevokedAt;
            if (!string.IsNullOrWhiteSpace(application.ClientId))
            {
                clients.Add(application.ClientId);
            }
        }

        if (!string.IsNullOrWhiteSpace(session.ClientId)
            && !string.Equals(session.ClientId, _options.IdentityClientId, StringComparison.Ordinal))
        {
            clients.Add(session.ClientId);
        }

        AddAudit(
            session.UserId,
            UserSecurityEventTypes.SessionRevoked,
            session.Id,
            session.DeviceId,
            session.Device?.DisplayName,
            session.ClientId,
            session.ApplicationName,
            request: null,
            details: reason,
            actorUserId: actorUserId);
    }

    private async Task RevokeTokensAsync(
        string userId,
        HashSet<string> clients,
        CancellationToken cancellationToken)
    {
        try
        {
            await tokenRevocationService.RevokeForClientsAsync(userId, clients, cancellationToken);
        }
        catch (Exception exception)
        {
            // Сеанс уже отозван в базе. Ошибка OpenIddict не должна скрывать
            // результат от пользователя: токены можно отозвать повторно.
            logger.LogError(
                exception,
                "Не удалось отозвать токены пользователя {UserId} для клиентов {Clients}.",
                userId,
                string.Join(", ", clients));
        }
    }

    private void AddAudit(
        string userId,
        string eventType,
        Guid? sessionId = null,
        Guid? deviceId = null,
        string? deviceName = null,
        string? clientId = null,
        string? applicationName = null,
        RequestContextInfo? request = null,
        string? details = null,
        string? actorUserId = null)
    {
        dbContext.Set<UserLoginAudit>().Add(new UserLoginAudit
        {
            UserId = userId,
            EventType = eventType,
            SessionId = sessionId,
            DeviceId = deviceId,
            DeviceName = deviceName,
            ClientId = clientId,
            ApplicationName = applicationName,
            IpAddress = request?.IpAddress,
            Location = request?.Location,
            UserAgent = request?.UserAgent,
            Details = string.IsNullOrWhiteSpace(actorUserId) ? details : $"{details} Действие: {actorUserId}.",
            OccurredAt = DateTimeOffset.UtcNow
        });
    }

    private static SignInPolicyDecision Allowed() =>
        new(SignInPolicyStatus.Allowed, null, []);

    private static string BuildConflictMessage(IReadOnlyList<SessionSummary> sessions)
    {
        var devices = string.Join(
            ", ",
            sessions
                .Select(session => $"«{session.DeviceName}» ({session.LastSeenAt.ToLocalTime():g})")
                .Take(3));

        return "У вас уже есть активный вход с другого устройства: " + devices + ". " +
               "Политика «одно устройство с подтверждением» требует сначала завершить старый сеанс. " +
               "Сделайте это на старом устройстве в разделе «Безопасность → Сеансы» " +
               "или попросите администратора завершить сеанс.";
    }

    private static string DescribePolicy(DeviceLoginPolicy policy) => policy switch
    {
        DeviceLoginPolicy.MultipleDevices => "вход с нескольких устройств разрешён",
        DeviceLoginPolicy.SingleDeviceNewestWins => "только одно устройство, новый вход завершает старый",
        DeviceLoginPolicy.SingleDeviceRequiresOldSessionEnd => "только одно устройство, старый сеанс нужно завершить вручную",
        _ => policy.ToString()
    };

    private static string DescribeAuthenticationMethod(string? method) => method switch
    {
        "pwd" => "пароль",
        "mfa" => "пароль и второй фактор",
        "passkey" => "ключ доступа",
        "external" => "внешний провайдер",
        null or "" => "не указан",
        _ => method
    };

    private static SessionSummary ToSummary(UserSession session) =>
        new(
            session.Id,
            session.UserId,
            session.DeviceId,
            session.Device?.DisplayName ?? "Устройство удалено",
            session.Device?.Browser,
            session.Device?.OperatingSystem,
            session.Device?.DeviceType,
            session.IpAddress,
            session.Location,
            session.ClientId,
            session.ApplicationName,
            session.CreatedAt,
            session.LastSeenAt,
            session.ExpiresAt,
            session.RevokedAt,
            session.RevokedBy,
            session.RevokeReason,
            session.IsActive && session.Device?.IsBlocked != true,
            session.Device?.IsBlocked ?? false,
            session.Applications
                .OrderBy(application => application.FirstSeenAt)
                .Select(application => new SessionApplicationSummary(
                    application.ClientId,
                    application.ApplicationName,
                    SplitScopes(application.Scopes),
                    application.FirstSeenAt,
                    application.LastSeenAt))
                .ToList());

    private static IReadOnlyList<string> SplitScopes(string? scopes) =>
        string.IsNullOrWhiteSpace(scopes)
            ? []
            : scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
