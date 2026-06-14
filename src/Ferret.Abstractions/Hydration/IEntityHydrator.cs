using System.Data.Common;

namespace Ferret.Abstractions.Hydration;

public interface IEntityHydrator
{
    Task<List<T>> HydrateAsync<T>(
        DbConnection connection,
        HydrationRequest request,
        CancellationToken ct)
        where T : class;
}
