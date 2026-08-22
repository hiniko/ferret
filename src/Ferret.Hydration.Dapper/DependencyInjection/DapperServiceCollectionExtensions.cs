using System.Data.Common;
using Ferret.Abstractions.Querying;
using Ferret.Core.Engine;
using Ferret.Hydration.Dapper.Querying;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Ferret.Hydration.Dapper.DependencyInjection;

public static class DapperServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IFerretQueryService"/> backed by Dapper, opening connections from the
    /// <see cref="NpgsqlDataSource"/> registered in DI (e.g. via <c>AddNpgsqlDataSource</c>).
    /// Call after <c>AddFerret(...)</c>.
    /// </summary>
    public static IServiceCollection AddFerretDapper(this IServiceCollection services)
        => services.AddFerretDapper(static async (sp, ct) =>
            await sp.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct));

    /// <summary>
    /// Registers <see cref="IFerretQueryService"/> backed by Dapper, opening connections with the
    /// supplied factory — for custom connection sources, pooling, or per-tenant routing.
    /// Call after <c>AddFerret(...)</c>.
    /// </summary>
    public static IServiceCollection AddFerretDapper(
        this IServiceCollection services,
        Func<IServiceProvider, CancellationToken, Task<DbConnection>> connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        // Replace (not Add) so calling this twice, or after another adapter, leaves exactly one
        // IFerretQueryService registration rather than stacking them.
        services.Replace(ServiceDescriptor.Scoped<IFerretQueryService>(sp =>
            new DapperQueryService(
                ct => connectionFactory(sp, ct),
                sp.GetRequiredService<IFerretEngine>(),
                sp.GetRequiredService<ISqlDialect>())));

        return services;
    }
}
