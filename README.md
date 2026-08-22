# Ferret

Postgres-first paging, filtering, sorting, and pluggable search for .NET. A drop-in alternative
to Typesense / Meilisearch / Elastic for projects that already run PostgreSQL.

[![NuGet](https://img.shields.io/nuget/v/Ferret.Core.svg)](https://www.nuget.org/packages/Ferret.Core)
![ci](https://github.com/hiniko/ferret/actions/workflows/ci.yml/badge.svg)

> **Status: `0.1` — early pre-1.0.** All backends are implemented and the core is exercised by a
> live origin project, but the library is not yet broadly battle-tested. The public API may change
> before `1.0.0`. See [VERSIONING.md](VERSIONING.md) for the pre-1.0 policy and road to 1.0.

## Features

- Pagination, filtering, sorting wired through a single query model.
- Trigram fuzzy search (`pg_trgm` GiST), full-text (`tsvector` / GIN), and pgvector ANN (HNSW)
  — opt-in, mix-and-match.
- Hybrid scoring (weighted RRF) across backends.
- Configurable embeddings — OpenAI-compatible, Ollama, or any Microsoft.Extensions.AI generator.
- **Schema is generated for you** from `[Searchable]` attributes via EF Core migrations
  (`Ferret.Migrations`) — or hand-roll it if you prefer.
- ORM-agnostic. EF Core or Dapper adapters ship in the box; bring your own session for any other stack.
- Attribute-driven entity discovery; pluggable naming strategy (snake_case / identity).
- ASP.NET Core model binders + one-line `MapFerret` endpoints with OpenAPI.

## Packages

| Package | Purpose |
| --- | --- |
| `Ferret.Abstractions` | Interfaces, attributes, query/result models. Reference from your domain assemblies. |
| `Ferret.Core` | Engine, SQL builder, attribute discovery, Postgres dialect, search backends. |
| `Ferret.Migrations` | EF Core migrations bridge — generates trigram/full-text/vector schema from annotations. |
| `Ferret.EntityFrameworkCore` | EF Core session + hydrator + DI registration. |
| `Ferret.Hydration.Dapper` | Dapper query service, session + hydrator for EF-free use. |
| `Ferret.AspNetCore` | `OffsetApiQuery` / `CursorApiQuery` + binders + `MapFerret`. |
| `Ferret.Hosting` | Reindex `BackgroundService` (drains `ferret_reindex_jobs`). |
| `Ferret.Tools.Cli` | `dotnet ferret reindex` CLI tool. |

## Install

```bash
dotnet add package Ferret.Core
dotnet add package Ferret.EntityFrameworkCore   # or: Ferret.Hydration.Dapper
```

## Quickstart — EF Core

Annotate the entity:

```csharp
using Ferret.Abstractions;

[SearchableEntity]
public sealed class Product : ISearchableEntity<Guid>
{
    public Guid Id { get; init; }

    [Searchable, Filterable, Sortable]
    public string Name { get; init; } = "";

    [Searchable(Weight = 2.0f)]
    public string Sku { get; init; } = "";
}
```

Wire up DI:

```csharp
services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
services.AddFerret(opts => opts
    .ScanAssembly(typeof(Product).Assembly)
    .UseTrigramSearch());
services.AddFerretEntityFrameworkCore<AppDbContext>();
```

Generate the schema (`Ferret.Migrations` — see [docs/migrations.md](docs/migrations.md)), then
inject `IFerretQueryService` (`Ferret.Abstractions.Querying`) and search:

```csharp
public sealed class ProductsController(IFerretQueryService search) : ControllerBase
{
    [HttpGet]
    public Task<OffsetResult<Product>> Get(CancellationToken ct) =>
        search.SearchOffsetAsync<Product, Guid>(new PagedQuery<Product, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "blue widge",
            Limit = 25,
            Page = 0,
            Sort = [new SortClause { Field = "Name", Direction = SortDirection.Ascending }],
        }, ct);
}
```

Cursor mode is available via `search.SearchCursorAsync<...>` returning `CursorResult<T>`.

## Quickstart — Dapper

```csharp
services.AddNpgsqlDataSource(connectionString);

services.AddFerret(opts => opts
    .ScanAssembly(typeof(Product).Assembly)
    .UseTrigramSearch());

services.AddFerretDapper();
```

Then inject the same `IFerretQueryService` the EF quickstart uses — application code is
identical either way:

```csharp
public sealed class ProductsController(IFerretQueryService search) : ControllerBase
{
    [HttpGet]
    public Task<OffsetResult<Product>> Get(CancellationToken ct) =>
        search.SearchOffsetAsync<Product, Guid>(
            new PagedQuery<Product, Guid> { Search = "widge", Limit = 25 }, ct);
}
```

See [docs/advanced.md](docs/advanced.md) for custom connection factories and the lower-level
`IFerretEngine` / session APIs.

## Documentation

- **[Setup](docs/setup.md)** — EF Core quickstart, Dapper/BYO pointers.
- **[Adding Ferret to an existing app](docs/integration.md)** — retrofitting into an app that
  already has an EF model, migrations, and controllers.
- **[Schema & migrations](docs/migrations.md)** — generated schema, reindex modes, the reindex
  worker/CLI, and the manual-SQL reference.
- **[Full-text search](docs/full-text-search.md)** — groups, weights, hybrid.
- **[Multisearch](docs/multisearch.md)** — grouped multi-entity search for cmd-K UIs.
- **[Vector search & embeddings](docs/vector-search.md)** — connectors, version transitions.
- **[Vector filtering](docs/vector-filtering.md)** — why filters + vector search need care, and what the ecosystem does about it.
- **[ASP.NET Core](docs/aspnetcore.md)** — query shapes, `MapFerret`, legacy wire shape.
- **[Lower-level APIs](docs/advanced.md)** — Dapper, `IFerretEngine`, bring-your-own session.

## Compatibility

- .NET 10 SDK
- EF Core 10 (when using `Ferret.EntityFrameworkCore` / `Ferret.Migrations`)
- PostgreSQL 15, 16, 17 — CI matrix runs all three
- Extensions: trigram needs `pg_trgm`; full-text needs none; vector needs `vector` (pgvector)
- `Ferret.Migrations` builds on
  [`EntityFrameworkCore.ExtensibleMigrations`](https://www.nuget.org/packages/EntityFrameworkCore.ExtensibleMigrations)
  ([source](https://github.com/hiniko/ExtensibleMigrations))

## License

MIT.
