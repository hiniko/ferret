using System.Data.Common;
using Ferret.Abstractions.Hydration;
using Ferret.Abstractions.Sql;

namespace Ferret.Abstractions.Session;

public interface IFerretSession : IAsyncDisposable
{
    ISqlDialect Dialect { get; }
    IEntityHydrator Hydrator { get; }

    Task<DbConnection> OpenConnectionAsync(CancellationToken ct);
}
