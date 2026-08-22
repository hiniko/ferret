# Setup

The default path is EF Core: Ferret manages hydration, schema, and reindexing, and your
application code talks to one interface, `IFerretQueryService`. Dapper and bring-your-own-session
are lower-level options for when you are not on EF Core for queries — see
[docs/advanced.md](advanced.md).

> Adding Ferret to an app you already have, rather than starting fresh? See
> [docs/integration.md](integration.md).

## EF Core

```csharp
services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
services.AddFerret(opts => opts
    .ScanAssembly(typeof(Product).Assembly)
    .UseTrigramSearch());          // + UseFullTextSearch / UseVectorSearch / UseHybridSearch
services.AddFerretEntityFrameworkCore<AppDbContext>();
```

`ScanAssembly` is chainable — call it once per assembly in a multi-project solution:

```csharp
services.AddFerret(opts => opts
    .ScanAssembly(typeof(Product).Assembly)
    .ScanAssembly(typeof(Article).Assembly)
    .UseTrigramSearch());
```

Schema is emitted from your `[Searchable]` attributes — see [migrations.md](migrations.md).
Inject `IFerretQueryService` (`Ferret.Abstractions.Querying`) and query:

```csharp
public sealed class ProductsController(IFerretQueryService search) : ControllerBase
{
    [HttpGet]
    public Task<OffsetResult<Product>> Get(CancellationToken ct) =>
        search.SearchOffsetAsync<Product, Guid>(query, ct);
}
```

Cursor mode is `search.SearchCursorAsync<...>`, returning `CursorResult<T>`. Both are the
only query methods on `IFerretQueryService`, alongside `MultiSearchAsync` — see
[multisearch.md](multisearch.md).

## Naming strategy

Column and table names come from an `INamingStrategy`, default `SnakeCaseNamingStrategy`
(`GivenName` -> `given_name`). Swap it with `UseNamingStrategy<T>()`:

```csharp
services.AddFerret(opts => opts
    .ScanAssembly(typeof(Product).Assembly)
    .UseNamingStrategy<IdentityNamingStrategy>()
    .UseTrigramSearch());
```

## Dapper and bring-your-own-session

Not using EF Core for queries, or integrating with a data stack Ferret has no adapter for?
See [docs/advanced.md](advanced.md) — it covers driving `IFerretEngine` directly with a
`DapperSession`, and implementing `IFerretSession` yourself. Schema generation
(`Ferret.Migrations`, [migrations.md](migrations.md)) works the same way regardless of which
session you query with.
