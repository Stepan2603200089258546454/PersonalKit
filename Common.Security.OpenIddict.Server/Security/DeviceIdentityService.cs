using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Common.Security.OpenIddict.Server.Security;

/// <summary>
/// Определяет устройство по постоянной cookie браузера.
/// </summary>
/// <remarks>
/// <para>
/// Cookie содержит случайные 32 байта. В базе хранится SHA-256-хеш, поэтому
/// значение cookie нельзя восстановить из базы. Cookie ставится на два года,
/// HttpOnly и SameSite=Lax: её нельзя прочитать из JavaScript, но она отправляется
/// при обычном переходе на сайт.
/// </para>
/// <para>
/// Если пользователь очистил cookie или зашёл в другом браузере, появится новое
/// устройство. Это ожидаемо: без cookie отличить «тот же браузер» от нового нельзя.
/// </para>
/// </remarks>
public sealed class DeviceIdentityService<TContext>(
    TContext dbContext,
    IOptions<UserSecurityOptions> options,
    ILogger<DeviceIdentityService<TContext>> logger) : IDeviceIdentityService
    where TContext : DbContext
{
    private readonly UserSecurityOptions _options = options.Value;
    private readonly Dictionary<string, DeviceResolution> _resolved = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<DeviceResolution> ResolveAsync(
        string userId,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(context);

        // За один HTTP-запрос устройство может понадобиться дважды: странице
        // входа для проверки политики и событию SigningIn для создания сеанса.
        // Кэш не даёт создать две записи, пока cookie ещё не вернулась в запросе.
        if (_resolved.TryGetValue(userId, out var cached))
        {
            return cached;
        }

        var cacheKey = $"{nameof(DeviceIdentityService<TContext>)}:{userId}";
        if (context.Items.TryGetValue(cacheKey, out var item) && item is DeviceResolution fromContext)
        {
            _resolved[userId] = fromContext;
            return fromContext;
        }

        var request = RequestContextInfo.FromHttpContext(context, _options.TrustForwardedHeaders);
        var userAgent = UserAgentInfo.Parse(request.UserAgent);

        var cookie = context.Request.Cookies[_options.DeviceCookieName];
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            var hash = Hash(cookie);
            var existing = await dbContext.Set<UserDevice>()
                .FirstOrDefaultAsync(
                    device => device.UserId == userId && device.KeyHash == hash,
                    cancellationToken);

            if (existing is not null)
            {
                existing.LastSeenAt = DateTimeOffset.UtcNow;
                existing.LastIpAddress = request.IpAddress;
                existing.LastLocation = request.Location;
                existing.UserAgent = request.UserAgent;
                existing.Browser ??= userAgent.Browser;
                existing.OperatingSystem ??= userAgent.OperatingSystem;
                existing.DeviceType ??= userAgent.DeviceType;

                await dbContext.SaveChangesAsync(cancellationToken);

                var resolution = new DeviceResolution(existing, IsNew: false, existing.IsBlocked);
                _resolved[userId] = resolution;
                context.Items[cacheKey] = resolution;
                return resolution;
            }
        }

        var newCookie = CreateCookieValue();
        var device = new UserDevice
        {
            UserId = userId,
            KeyHash = Hash(newCookie),
            DisplayName = userAgent.DisplayName,
            Browser = userAgent.Browser,
            OperatingSystem = userAgent.OperatingSystem,
            DeviceType = userAgent.DeviceType,
            FirstIpAddress = request.IpAddress,
            LastIpAddress = request.IpAddress,
            LastLocation = request.Location,
            UserAgent = request.UserAgent,
            CreatedAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow
        };

        dbContext.Set<UserDevice>().Add(device);
        await dbContext.SaveChangesAsync(cancellationToken);

        SetCookie(context, newCookie);
        logger.LogInformation(
            "Для пользователя {UserId} создано новое устройство {DeviceId} ({DeviceName}).",
            userId,
            device.Id,
            device.DisplayName);

        var created = new DeviceResolution(device, IsNew: true, IsBlocked: false);
        _resolved[userId] = created;
        context.Items[cacheKey] = created;
        return created;
    }

    /// <inheritdoc />
    public async Task<UserDevice?> FindAsync(
        string userId,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var cookie = context.Request.Cookies[_options.DeviceCookieName];
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return null;
        }

        var hash = Hash(cookie);
        return await dbContext.Set<UserDevice>()
            .FirstOrDefaultAsync(
                device => device.UserId == userId && device.KeyHash == hash,
                cancellationToken);
    }

    private void SetCookie(HttpContext context, string value)
    {
        context.Response.Cookies.Append(
            _options.DeviceCookieName,
            value,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.Add(_options.DeviceCookieLifetime)
            });
    }

    private static string CreateCookieValue()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
