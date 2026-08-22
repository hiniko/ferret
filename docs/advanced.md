# Advanced usage

The rest of the documentation shows one path: inject `IFerretQueryService` and call it.
That is the right answer for almost all application code.

This page covers the layers underneath it — for custom adapters, non-EF data access, and
cases where you need to control session and connection lifetime yourself.

## The layers

```
IFerretQueryService          ← application code. One interface, all operations.
        │
        ├─ EntityFrameworkQueryService<TContext>   (registered by AddFerretEntityFrameworkCore)
        │
        ▼
IFerretEngine + IFerretSession   ← the foundation. Session owns a connection.
        │
        ▼
   SQL builders / backends
```

`IFerretQueryService` is a thin wrapper: it constructs a session from the registered
context and dialect, then calls the engine. Everything it does, you can do by hand.

## Calling the engine directly

Use this when you need to control the session — a connection you already opened, an
ambient transaction, a non-EF data source, or a custom `IEntityHydrator`.

```csharp
public sealed class UserSearch(IFerretEngine engine, ISqlDialect dialect, AppDbContext db)
{
    public async Task<OffsetResult<User>> Search(string term, CancellationToken ct)
    {
        await using var session = new EntityFrameworkSession(db, dialect);
        return await engine.SearchOffsetAsync<User, Guid>(session, new PagedQuery<User, Guid>
        {
            Search = term,
            Limit = 25,
        }, ct);
    }
}
```

A session owns a connection and is built per call — never register one as a singleton or
share one across requests.

`IFerretEngine` exposes `SearchOffsetAsync`, `SearchCursorAsync`, `MultiSearchAsync`, and
`ReindexAsync`. The session-taking overloads are the primitives; `IFerretQueryService` is
sugar over them.

## Dapper and raw Npgsql

Dapper users get the same injected `IFerretQueryService` as EF users — register it with
`AddFerretDapper`:

```csharp
services.AddNpgsqlDataSource(connectionString);

services.AddFerret(o => o
    .ScanAssembly(typeof(User).Assembly)
    .UseTrigramSearch());

services.AddFerretDapper();          // opens connections from the registered NpgsqlDataSource
```

Then inject `IFerretQueryService` exactly as the EF samples do — application code does not
know or care which adapter is underneath.

For a custom connection source — your own pooling, per-tenant routing, an ambient
transaction — supply the factory instead:

```csharp
services.AddFerretDapper((sp, ct) =>
    sp.GetRequiredService<IMyConnectionSource>().OpenAsync(ct));
```

The factory runs per query, and the connection is disposed with the session.

### Driving the engine directly

`DapperSession` remains public if you want to skip the query service and hold the session
yourself:

```csharp
await using var session = new DapperSession(
    async token => await source.OpenConnectionAsync(token),
    dialect);

return await engine.SearchOffsetAsync<User, Guid>(session, query, ct);
```

`DapperSession` supplies its own `DapperHydrator`, opens the connection lazily on first
use, and disposes it with the session.

## Bring your own session

`IFerretSession` has three members:

```csharp
public interface IFerretSession : IAsyncDisposable
{
    ISqlDialect Dialect { get; }
    IEntityHydrator Hydrator { get; }
    Task<DbConnection> OpenConnectionAsync(CancellationToken ct);
}
```

Implement it to plug in any connection source or materialisation strategy — a pooled
connection you manage, a connection enlisted in an ambient transaction, or a hydrator that
maps to DTOs instead of entities. Pair it with `AddFerret(...)` alone and drive
`IFerretEngine` directly.

## Registering the query service without key auto-fill

`AddFerretEntityFrameworkCore<TContext>` is the blessed EF registration. It registers the
query service *and* an `IEntityKeyOverrideSource` that reads primary keys — including
composite keys — from the EF model, so `[SearchableEntity]` does not have to name them.

`AddFerretEntityFrameworkQueryService<TContext>` is the lower-level seam. It exists for
custom adapters that want the EF query service registration without committing to the rest.
It uses `services.Replace(...)`, so calling it after another adapter leaves exactly one
`IFerretQueryService` registration rather than stacking them.

Most applications should call `AddFerretEntityFrameworkCore<TContext>`.

## Composite keys

Ferret supports composite keys throughout, including cursor pagination (the cursor encodes
all key columns in order).

With the EF adapter, keys are read from the model automatically. Without it — Dapper, BYO
session — name them explicitly:

```csharp
[SearchableEntity(KeyProperties = ["TenantId", "Id"])]
public class TenantDocument { /* ... */ }
```

Order matters: it is the order used for tiebreaking and cursor encoding.

## Forcing a backend per query

An entity annotated for several backends routes through hybrid fusion by default. To
bypass fusion for one query:

```csharp
new PagedQuery<Article, Guid> { Search = term, Backend = SearchBackend.FullText }
```

`Backend = null` (the default) uses normal routing. See
[full-text-search.md](full-text-search.md) for hybrid configuration.

## Observability

```csharp
services.AddFerret(o => o
    .WithSlowQueryThreshold(250)      // log queries slower than 250ms
    .WithStatementLogging());         // log generated SQL — development only
```

`WithStatementLogging` writes full statements including parameter values. Do not enable it
in production where search terms may contain personal data.

## Naming strategies

Ferret maps CLR names to database identifiers through an `INamingStrategy`.
`SnakeCaseNamingStrategy` is the default (`GivenName` → `given_name`).

```csharp
services.AddFerret(o => o.UseNamingStrategy<IdentityNamingStrategy>());
```

`IdentityNamingStrategy` passes names through unchanged. Implement `INamingStrategy` for
anything else. Per-entity and per-property overrides via `[SearchableEntity(Table = ...)]`
take precedence over the strategy.

Naming mismatches surface as empty results, not errors — the SQL is valid, it just
references the wrong identifiers. Verify against one entity before rolling out widely.
