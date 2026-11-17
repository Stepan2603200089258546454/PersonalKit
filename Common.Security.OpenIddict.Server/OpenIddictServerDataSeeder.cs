using OpenIddict.Abstractions;

namespace Common.Security.OpenIddict.Server;

/// <summary>
/// Создаёт области и клиентов OpenIddict по описаниям приложения.
/// </summary>
/// <remarks>
/// Библиотека не знает, какие именно приложения подключаются к Identity:
/// приложение передаёт <see cref="OpenIddictScopeDescriptor"/> и
/// <see cref="OpenIddictApplicationDescriptor"/>. Здесь остаётся общая логика
/// «найти по имени, создать или обновить», чтобы не дублировать её в каждом проекте.
/// </remarks>
public static class OpenIddictServerDataSeeder
{
    /// <summary>Создаёт область или обновляет существующую.</summary>
    /// <param name="manager">Менеджер областей OpenIddict.</param>
    /// <param name="descriptor">Описание области.</param>
    /// <param name="refresh">Обновлять существующую область.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public static async Task EnsureScopeAsync(
        IOpenIddictScopeManager manager,
        OpenIddictScopeDescriptor descriptor,
        bool refresh,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Name);

        var existing = await manager.FindByNameAsync(descriptor.Name, cancellationToken);
        if (existing is null)
        {
            await manager.CreateAsync(descriptor, cancellationToken);
            return;
        }

        if (refresh)
        {
            await manager.UpdateAsync(existing, descriptor, cancellationToken);
        }
    }

    /// <summary>Создаёт клиента или обновляет существующего.</summary>
    /// <param name="manager">Менеджер клиентов OpenIddict.</param>
    /// <param name="descriptor">Описание клиента.</param>
    /// <param name="refresh">Обновлять существующего клиента.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public static async Task EnsureApplicationAsync(
        IOpenIddictApplicationManager manager,
        OpenIddictApplicationDescriptor descriptor,
        bool refresh,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ClientId);

        var existing = await manager.FindByClientIdAsync(descriptor.ClientId, cancellationToken);
        if (existing is null)
        {
            await manager.CreateAsync(descriptor, cancellationToken);
            return;
        }

        if (refresh)
        {
            await manager.UpdateAsync(existing, descriptor, cancellationToken);
        }
    }
}
