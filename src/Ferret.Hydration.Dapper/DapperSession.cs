using System.Data.Common;
using Ferret.Abstractions;

namespace Ferret.Hydration.Dapper;

public sealed class DapperSession : IFerretSession
{
    private readonly Func<CancellationToken, Task<DbConnection>> _connectionFactory;
    private DbConnection? _connection;

    public DapperSession(Func<CancellationToken, Task<DbConnection>> connectionFactory, ISqlDialect dialect)
    {
        _connectionFactory = connectionFactory;
        Dialect = dialect;
        Hydrator = new DapperHydrator();
    }

    public ISqlDialect Dialect { get; }
    public IEntityHydrator Hydrator { get; }

    public async Task<DbConnection> OpenConnectionAsync(CancellationToken ct)
    {
        _connection ??= await _connectionFactory(ct);
        if (_connection.State != System.Data.ConnectionState.Open) await _connection.OpenAsync(ct);
        return _connection;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
    }
}
