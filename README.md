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
| `Ferret.EntityFrameworkCore` | EF Core session + hydrator + search extensions. |
| `Ferret.Hydration.Dapper` | Dapper-based session + hydrator for EF-free use. |
| `Ferret.AspNetCore` | `OffsetApiQuery` / `CursorApiQuery` + binders + `MapFerret`. |
| `Ferret.Hosting` | Reindex `BackgroundService` (drains `ferret_reindex_jobs`). |
| `Ferret.Tools.Cli` | `dotnet ferret reindex` CLI tool. |
| `Ferret.Compat.LegacyApi` | Wire-shape adapter for clients using the original PagedQuery JSON shape. |

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
```

Generate the schema (`Ferret.Migrations` — see [docs/migrations.md](docs/migrations.md)), then
search:

```csharp
var page = await dbContext.SearchOffsetAsync<Product, Guid>(serviceProvider,
    new PagedQuery<Product, Guid>
    {
        Mode = PaginationMode.Offset,
        Search = "blue widge",
        Limit = 25,
        Page = 0,
        Sort = [new SortClause { Field = "Name", Direction = SortDirection.Ascending }],
    });
```

Cursor mode is available via `dbContext.SearchCursorAsync<...>` returning `CursorResult<T>`.

## Quickstart — Dapper

```csharp
services.AddFerret(opts => opts
    .ScanAssembly(typeof(Product).Assembly)
    .UseTrigramSearch()
    .UseDapperHydration());

await using var session = new DapperSession(
    ct => Task.FromResult<DbConnection>(new NpgsqlConnection(connectionString)),
    sp.GetRequiredService<ISqlDialect>());

var page = await sp.GetRequiredService<IFerretEngine>()
    .SearchOffsetAsync<Product, Guid>(session,
        new PagedQuery<Product, Guid> { Mode = PaginationMode.Offset, Search = "widge", Limit = 25 });
```

## Documentation

- **[Setup & integration modes](docs/setup.md)** — EF Core, Dapper, or bring-your-own-session.
- **[Schema & migrations](docs/migrations.md)** — generated schema, reindex modes, the reindex
  worker/CLI, and the manual-SQL reference.
- **[Full-text search](docs/full-text-search.md)** — groups, weights, hybrid.
- **[Vector search & embeddings](docs/vector-search.md)** — connectors, version transitions.
- **[ASP.NET Core](docs/aspnetcore.md)** — query shapes, `MapFerret`, legacy wire shape.

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
