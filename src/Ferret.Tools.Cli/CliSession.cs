using System.Data.Common;
using Ferret.Abstractions.Hydration;
using Ferret.Abstractions.Session;
using Ferret.Abstractions.Sql;
using Npgsql;

namespace Ferret.Tools.Cli;

internal sealed class CliSession : IFerretSession
{
    private readonly string _connectionString;
    private NpgsqlConnection? _connection;

    public CliSession(string connectionString, ISqlDialect dialect)
    {
        _connectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            PersistSecurityInfo = true,
        }.ConnectionString;
        Dialect = dialect;
        Hydrator = new UnsupportedHydrator();
    }

    public ISqlDialect Dialect { get; }
    public IEntityHydrator Hydrator { get; }

    public async Task<DbConnection> OpenConnectionAsync(CancellationToken ct)
    {
        _connection ??= new NpgsqlConnection(_connectionString);
        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync(ct);
        return _connection;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }

    private sealed class UnsupportedHydrator : IEntityHydrator
    {
        public Task<List<T>> HydrateAsync<T>(DbConnection connection, HydrationRequest request, CancellationToken ct)
            where T : class
            => throw new NotSupportedException("The Ferret CLI does not hydrate entities.");
    }
}
