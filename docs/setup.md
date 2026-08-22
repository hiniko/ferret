# Setup

The default path is EF Core: Ferret manages hydration, schema, and reindexing, and your
application code talks to one interface, `IFerretQueryService`. Dapper gets the same interface
via `AddFerretDapper()`; bring-your-own-session is the lower-level option for stacks Ferret has
no adapter for — both in [docs/advanced.md](advanced.md).

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

Not using EF Core for queries? `AddFerretDapper()` registers the same `IFerretQueryService`
your controllers already inject, so nothing above the data layer changes. For a stack Ferret
has no adapter for, implement `IFerretSession` yourself. Both are covered in
[docs/advanced.md](advanced.md). Schema generation
(`Ferret.Migrations`, [migrations.md](migrations.md)) works the same way regardless of which
session you query with.
