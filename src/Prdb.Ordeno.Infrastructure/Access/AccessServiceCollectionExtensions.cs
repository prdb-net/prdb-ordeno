using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Prdb.Ordeno.Infrastructure.Persistence;

namespace Prdb.Ordeno.Infrastructure.Access;

public static class AccessServiceCollectionExtensions
{
    public static IServiceCollection AddOrdenoAccess(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPasswordHasher<StoredConfiguration>, PasswordHasher<StoredConfiguration>>();
        services.AddScoped<AccessService>();

        return services;
    }

    /// <summary>
    /// Forgets the password and every session at startup. ADR 0010 wants a way
    /// back in for someone who lost the password, and this is it — reachable
    /// only by whoever can edit how the container is started.
    /// </summary>
    public static async Task ResetOrdenoAccessAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<AccessService>().ResetAsync(cancellationToken);
    }
}
