using System.Data.Common;
using Ferret.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Ferret.EntityFrameworkCore;

public sealed class EntityFrameworkSession : IFerretSession
{
    private readonly DbContext _context;
    private bool _connectionOpened;

    public EntityFrameworkSession(DbContext context, ISqlDialect dialect)
    {
        _context = context;
        Dialect = dialect;
        Hydrator = new EntityFrameworkHydrator(context);
    }

    public ISqlDialect Dialect { get; }
    public IEntityHydrator Hydrator { get; }

    public async Task<DbConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var conn = _context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await _context.Database.OpenConnectionAsync(ct);
            _connectionOpened = true;
        }
        return conn;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connectionOpened)
        {
            await _context.Database.CloseConnectionAsync();
        }
    }
}
