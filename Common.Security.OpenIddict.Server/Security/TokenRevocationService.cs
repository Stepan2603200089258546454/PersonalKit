using OpenIddict.Abstractions;

namespace Common.Security.OpenIddict.Server.Security;

/// <summary>
/// Отзывает токены и авторизации OpenIddict, связанные с сеансом.
/// </summary>
public interface ITokenRevocationService
{
    /// <summary>Отзывает токены пользователя для указанных клиентов.</summary>
    Task RevokeForClientsAsync(
        string subject,
        IEnumerable<string> clientIds,
        CancellationToken cancellationToken = default);

    /// <summary>Отзывает все токены и авторизации пользователя.</summary>
    Task RevokeAllAsync(string subject, CancellationToken cancellationToken = default);
}

/// <summary>
/// Реализация отзыва через менеджеры OpenIddict.
/// </summary>
/// <remarks>
/// OpenIddict не хранит ClientId в токене: токен ссылается на приложение по
/// внутреннему идентификатору. Поэтому для каждого токена берётся ApplicationId,
/// затем ClientId приложения. Для домашнего сервера объём токенов небольшой;
/// при большом количестве стоит хранить ClientId в свойствах токена.
/// </remarks>
public sealed class TokenRevocationService(
    IOpenIddictTokenManager tokenManager,
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictApplicationManager applicationManager,
    ILogger<TokenRevocationService> logger) : ITokenRevocationService
{
    /// <inheritdoc />
    public async Task RevokeForClientsAsync(
        string subject,
        IEnumerable<string> clientIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        var clients = clientIds
            .Where(clientId => !string.IsNullOrWhiteSpace(clientId))
            .ToHashSet(StringComparer.Ordinal);

        if (clients.Count == 0)
        {
            return;
        }

        var applicationIds = new Dictionary<string, string?>(StringComparer.Ordinal);

        await foreach (var token in tokenManager.FindBySubjectAsync(subject, cancellationToken))
        {
            var applicationId = await tokenManager.GetApplicationIdAsync(token, cancellationToken);
            if (applicationId is null || !await MatchesAsync(applicationId, clients, applicationIds, cancellationToken))
            {
                continue;
            }

            await tokenManager.TryRevokeAsync(token, cancellationToken);
        }

        await foreach (var authorization in authorizationManager.FindBySubjectAsync(subject, cancellationToken))
        {
            var applicationId = await authorizationManager.GetApplicationIdAsync(authorization, cancellationToken);
            if (applicationId is null || !await MatchesAsync(applicationId, clients, applicationIds, cancellationToken))
            {
                continue;
            }

            await authorizationManager.TryRevokeAsync(authorization, cancellationToken);
        }

        logger.LogInformation(
            "Отозваны токены пользователя {Subject} для клиентов: {Clients}.",
            subject,
            string.Join(", ", clients));
    }

    /// <inheritdoc />
    public async Task RevokeAllAsync(string subject, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        await tokenManager.RevokeBySubjectAsync(subject, cancellationToken);
        await authorizationManager.RevokeBySubjectAsync(subject, cancellationToken);

        logger.LogInformation("Отозваны все токены и авторизации пользователя {Subject}.", subject);
    }

    private async Task<bool> MatchesAsync(
        string applicationId,
        HashSet<string> clients,
        Dictionary<string, string?> cache,
        CancellationToken cancellationToken)
    {
        if (!cache.TryGetValue(applicationId, out var clientId))
        {
            var application = await applicationManager.FindByIdAsync(applicationId, cancellationToken);
            clientId = application is null
                ? null
                : await applicationManager.GetClientIdAsync(application, cancellationToken);
            cache[applicationId] = clientId;
        }

        return clientId is not null && clients.Contains(clientId);
    }
}
