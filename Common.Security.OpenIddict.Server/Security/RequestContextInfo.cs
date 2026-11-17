using System.Net;
using System.Net.Sockets;

namespace Common.Security.OpenIddict.Server.Security;

/// <summary>
/// Сведения о запросе, которые сохраняются в сеансе и журнале.
/// </summary>
/// <param name="IpAddress">IP-адрес клиента.</param>
/// <param name="Location">Локация: из заголовков прокси или подпись частного адреса.</param>
/// <param name="UserAgent">User agent браузера.</param>
public sealed record RequestContextInfo(string? IpAddress, string? Location, string? UserAgent)
{
    /// <summary>Собирает сведения из HTTP-запроса.</summary>
    /// <param name="context">Текущий HTTP-запрос.</param>
    /// <param name="trustForwardedHeaders">
    /// Доверять ли <c>X-Forwarded-For</c> и геозаголовкам. Включайте только за
    /// доверенным reverse proxy: иначе клиент подделает адрес.
    /// </param>
    public static RequestContextInfo FromHttpContext(HttpContext context, bool trustForwardedHeaders = false)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ipAddress = context.Connection.RemoteIpAddress;

        if (trustForwardedHeaders)
        {
            var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var first = forwarded.Split(',', 2)[0].Trim();
                if (IPAddress.TryParse(first, out var parsed))
                {
                    ipAddress = parsed;
                }
            }
        }

        var ipText = ipAddress?.ToString();
        var location = trustForwardedHeaders ? ReadLocation(context) : null;
        location ??= DescribeAddress(ipAddress);

        return new RequestContextInfo(
            ipText,
            location,
            context.Request.Headers.UserAgent.ToString());
    }

    private static string? ReadLocation(HttpContext context)
    {
        var country = context.Request.Headers["CF-IPCountry"].FirstOrDefault()
            ?? context.Request.Headers["X-Country-Code"].FirstOrDefault()
            ?? context.Request.Headers["X-Geo-Country"].FirstOrDefault();

        var city = context.Request.Headers["X-Geo-City"].FirstOrDefault()
            ?? context.Request.Headers["X-City"].FirstOrDefault();

        var parts = new[] { city, country }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parts.Length == 0 ? null : string.Join(", ", parts);
    }

    private static string? DescribeAddress(IPAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        if (IPAddress.IsLoopback(address))
        {
            return "этот компьютер";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return IsPrivate(address) ? "локальная сеть" : null;
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
        }

        return false;
    }
}
