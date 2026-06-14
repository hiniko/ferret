# Ferret

Postgres-first paging, filtering, sorting, and pluggable search for .NET. A drop-in alternative to Typesense / Meilisearch / Elastic for projects that already run PostgreSQL.

[![NuGet](https://img.shields.io/nuget/v/Ferret.Core.svg)](https://www.nuget.org/packages/Ferret.Core)
![ci](https://github.com/hiniko/paged-query/actions/workflows/ci.yml/badge.svg)

> **Status: `0.1` — early pre-1.0.** All backends are implemented and the core is exercised by a live
> origin project, but the library is not yet broadly battle-tested. The public API may change
> before `1.0.0`. See [VERSIONING.md](VERSIONING.md) for the pre-1.0 policy and road to 1.0.

## Features

- Pagination, filtering, sorting wired through a single query model.
- Trigram fuzzy search backed by `pg_trgm` GiST indexes.
- Full-text search (`tsvector` / `tsquery`) and pgvector ANN — opt-in, mix-and-match.
- Hybrid scoring (RRF) across backends.
- ORM-agnostic. EF Core or Dapper adapters ship in the box; bring your own session for any other stack.
- Attribute-driven entity discovery — interface implementation is optional.
- Pluggable naming strategy (snake_case by default, identity-mode for legacy schemas).
- ASP.NET Core model binders for compact `?filter=field:op:value&sort=field:dir` URL shapes.

## Packages

| Package | Purpose |
| --- | --- |
| `Ferret.Abstractions` | Interfaces, attributes, query/result models. Reference this from your domain assemblies. |
| `Ferret.Core` | Engine, SQL builder, attribute discovery, Postgres dialect, trigram backend. |
| `Ferret.EntityFrameworkCore` | EF Core session + hydrator + `CrudRepository<T, TKey>` + `IHasTimestamps`. |
| `Ferret.Hydration.Dapper` | Dapper-based session + hydrator for EF-free use. |
| `Ferret.AspNetCore` | `OffsetApiQuery` / `CursorApiQuery` + model binders for query strings. |
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
    .UsePostgres()
    .UseTrigramSearch());
```

Search from a controller or service:

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
    .UsePostgres()
    .UseTrigramSearch()
    .UseDapperHydration());

await using var session = new DapperSession(
    ct => Task.FromResult<DbConnection>(new NpgsqlConnection(connectionString)),
    sp.GetRequiredService<ISqlDialect>());

var page = await sp.GetRequiredService<IFerretEngine>()
    .SearchOffsetAsync<Product, Guid>(session,
        new PagedQuery<Product, Guid> { Mode = PaginationMode.Offset, Search = "widge", Limit = 25 });
```

## Schema

You're responsible for creating the indexes Ferret expects. For trigram:

```sql
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE INDEX CONCURRENTLY products_name_gist_trgm ON products USING gist (name gist_trgm_ops);
CREATE INDEX CONCURRENTLY products_sku_gist_trgm  ON products USING gist (sku  gist_trgm_ops);
```

A `Ferret.Schema` library + `dotnet ferret-schema` CLI for emitting these via DDL or EF migrations are planned.

### Auto-generate indexes via EF Core migrations

Install:

```bash
dotnet add package Ferret.Migrations
```

Add the annotation hook to `OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    // … your usual entity configuration …
    modelBuilder.UseFerretSearchableAnnotations(typeof(Product).Assembly);
}
```

Wire the design-time services. Create `<YourProjectRoot>/MigrationsDesignTimeServices.cs`:

```csharp
using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

public sealed class MigrationsDesignTimeServices : IDesignTimeServices
{
    public void ConfigureDesignTimeServices(IServiceCollection services)
    {
        // Sister bridge scans loaded assemblies for [CustomMigrationHandler]-marked
        // types and registers them — Ferret's three handlers auto-wire here.
        new ExtensibleMigrationsDesignTimeServices().ConfigureDesignTimeServices(services);
    }
}
```

`dotnet ef migrations add InitialFerretIndexes` will scaffold a migration that creates
the `pg_trgm` extension and the GiST `gist_trgm_ops` indexes for every `[Searchable]` property.

> If you prefer explicit handler registration over assembly auto-scan, call
> `services.AddFerretMigrations()` instead of `ExtensibleMigrationsDesignTimeServices`.
> Don't call both — that double-registers each handler.

## Full-text search

```csharp
[SearchableEntity]
[SearchGroup("content", FullTextConfig = "english")]
public sealed class Article : ISearchableEntity<Guid>
{
    public Guid Id { get; init; }

    [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 2.0f)]
    public string Title { get; init; } = "";

    [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 1.0f)]
    public string Body { get; init; } = "";
}
```

```csharp
services.AddFerret(o => o
    .ScanAssembly(typeof(Article).Assembly)
    .UsePostgres()
    .UseFullTextSearch(ft => ft.DefaultConfig = "english"));
```

Schema is emitted by `Ferret.Migrations` from your `[Searchable]` attributes — `dotnet ef migrations add` produces the sidecar table, GIN-indexed `tsvector` columns, sync function, and trigger automatically. You can keep hand-rolled SQL if you prefer (shape shown below for reference):

```sql
CREATE TABLE articles_search (
    id          uuid PRIMARY KEY REFERENCES articles(id) ON DELETE CASCADE,
    content_tsv tsvector,
    updated_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_articles_search_content_tsv_gin ON articles_search USING gin (content_tsv);

CREATE OR REPLACE FUNCTION articles_search_sync() RETURNS trigger AS $$
BEGIN
    INSERT INTO articles_search (id, content_tsv, updated_at)
    VALUES (NEW.id,
        setweight(to_tsvector('english', coalesce(NEW.title, '')), 'A') ||
        setweight(to_tsvector('english', coalesce(NEW.body,  '')), 'B'),
        now())
    ON CONFLICT (id) DO UPDATE
    SET content_tsv = EXCLUDED.content_tsv, updated_at = now();
    RETURN NEW;
END $$ LANGUAGE plpgsql;

CREATE TRIGGER articles_search_sync_t
    AFTER INSERT OR UPDATE OF title, body ON articles
    FOR EACH ROW EXECUTE FUNCTION articles_search_sync();
```

Multiple `[Searchable(..., Group = "...")]` attributes per property are supported — declare different groups (e.g. `content`, `tags`) for hybrid coverage. Per-query parser, group selection, and backend choice are configured via `UseFullTextSearch(...)`, not via the request; the `PagedQuery.Search` field is the only request-side knob.

Note: the sidecar PK column reuses the source entity's key column name (e.g. `id`), not a generic `entity_id`. `Ferret.Migrations` preserves this convention.

`ReindexMode` (per group via `[SearchGroup(..., Reindex = ...)]` or globally via `FullTextOptions.DefaultReindex`):
- `Inline` (default) — migration runs the backfill `INSERT … SELECT` in one transaction. Best for small tables and dev.
- `Concurrent` — migration enqueues a row into `ferret_reindex_jobs`. **Note:** the worker that drains this queue ships in a follow-up release; until then, `Concurrent` will leave the sidecar empty after migration unless you backfill manually. Prefer `Inline` for now.
- `Deferred` — migration emits schema + trigger only. Backfill is your responsibility.

## ASP.NET Core query shape

Two non-generic request types — `OffsetApiQuery` and `CursorApiQuery` — bind compact
query strings. Each maps to a `PagedQuery<T, TKey>` at the controller boundary, so
OpenAPI shows exactly one shape per endpoint with no cross-mode noise.

### Offset

```
GET /products?page=0&limit=25&q=widge&filter=Name:eq:Blue&sort=Name:asc
```

```csharp
[HttpGet, Route("products")]
public Task<OffsetResult<Product>> Get(
    [FromQuery] OffsetApiQuery q,
    PaginationDefaultsResolver resolver,
    IServiceProvider sp,
    CancellationToken ct)
{
    var defaults = resolver.Resolve(HttpContext);
    return db.SearchOffsetAsync<Product, Guid>(sp, q.ToPagedQuery<Product, Guid>(defaults), ct);
}
```

### Cursor

```
GET /products?after=<token>&limit=25&q=widge&sort=Name:asc
```

```csharp
[HttpGet, Route("products/stream")]
public Task<CursorResult<Product>> Stream(
    [FromQuery] CursorApiQuery q,
    PaginationDefaultsResolver resolver,
    IServiceProvider sp,
    CancellationToken ct)
{
    var defaults = resolver.Resolve(HttpContext);
    return db.SearchCursorAsync<Product, Guid>(sp, q.ToPagedQuery<Product, Guid>(defaults), ct);
}
```

Field names in `filter` / `sort` are CLR property names (PascalCase) per the v1 binder.
Filter operators: `eq`, `neq`, `contains`, `gt`, `gte`, `lt`, `lte`, `in`. The `in`
operator takes a comma-separated value list (e.g. `filter=Category:in:tools,garden`).

### Pagination defaults

Configure globally:

```csharp
services.AddFerret(opts => opts.WithPaginationDefaults(defaultLimit: 25, maxLimit: 100));
services.AddFerretAspNetCore();   // registers PaginationDefaultsResolver
```

Override per endpoint with `[PaginationLimits(Default = 50, Max = 200)]`.

### Legacy wire shape

If you're migrating an existing service whose clients depend on the legacy parameter
names (`page`, `page_size`, `search`, `search_fields`, `include_match_info`,
`include_hidden`) and response JSON shape (`items`, `page`, `count`, `total`,
`match_info`), add a reference to `Ferret.Compat.LegacyApi` and bind `LegacyApiQuery`
+ return `LegacyPagedResponse<T>` via `result.ToLegacyResponse(p => p.Id)`. See
`samples/Ferret.Example.LegacyApi` for a working endpoint.

## ASP.NET Core — MapFerret

`MapFerret<T, TKey>` scaffolds a complete JSON query endpoint in one line — binding,
validation, paging, and OpenAPI metadata included.

```csharp
app.MapFerret<Product, Guid>("/api/products");
```

### Prerequisites

```csharp
builder.Services.AddFerret(opts => opts
    .ScanAssembly(typeof(Product).Assembly)
    .UsePostgres()
    .UseTrigramSearch());
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddFerretEntityFrameworkQueryService<AppDbContext>(); // IFerretQueryService
builder.Services.AddFerretAspNetCore();                                // PaginationDefaultsResolver
```

The endpoint resolves `IFerretQueryService` from DI, so any backend (EF Core, Dapper,
or the in-process core service) works without changing the route.

### Query-string grammar

| Parameter    | Applies to | Meaning |
|--------------|------------|---------|
| `q`          | both       | Full-text / trigram search term |
| `fields`     | both       | Comma-separated property names to search |
| `match_info` | both       | `true` to include search match metadata |
| `filter`     | both       | `Field:op:value`, repeatable. Ops: `eq`, `neq`, `contains`, `gt`, `gte`, `lt`, `lte`, `in` (comma-separated values) |
| `sort`       | both       | `Field` or `Field:asc` / `Field:desc`, repeatable |
| `limit`      | both       | Page size; rejected with `400` if above the configured max |
| `page`       | offset     | 1-based page number |
| `count`      | offset     | `false` to skip the total-count query |
| `after`      | cursor     | Token to page forward from |
| `before`     | cursor     | Token to page backward from |

Field names in `filter` / `sort` / `fields` are CLR property names (PascalCase).

### Offset request + response

```
GET /api/products?filter=Name:contains:Widget&sort=Name:asc&limit=2&count=true
```

```json
{
  "items": [
    { "id": "…", "name": "Blue Widget",  "sku": "BLUE-001" },
    { "id": "…", "name": "Green Widget", "sku": "GRN-002" }
  ],
  "limit": 2,
  "page": 0,
  "totalCount": 3,
  "hasMore": true,
  "hasPrev": false
}
```

### Cursor request + response

```
GET /api/products?sort=Name:asc&limit=2&after=<nextCursor>
```

```json
{
  "items": [
    { "id": "…", "name": "Red Widget", "sku": "RED-001" }
  ],
  "limit": 2,
  "nextCursor": null,
  "prevCursor": "…",
  "hasMore": false,
  "hasPrev": true
}
```

Feed `nextCursor` back as `after` to page forward and `prevCursor` as `before` to page
backward. An invalid or tampered token returns `400 Problem`.

### Cursor switch and sibling routes

Pick the mode per endpoint with the `configure` overload:

```csharp
app.MapFerret<Product, Guid>("/api/products");                                  // offset (default)
app.MapFerret<Product, Guid>("/api/products/stream", o =>                        // cursor sibling route
    o.Pagination = FerretEndpointPaginationMode.Cursor);
```

Exposing both an offset and a cursor route over the same entity is a common pattern:
offset for paged UIs, cursor for stable infinite-scroll / export feeds. Each route
advertises only its own parameters, so there is no cross-mode noise.

### OpenAPI

When `AddOpenApi()` is registered, `MapFerret` attaches an operation transformer that
documents every query parameter for the chosen mode and declares the `200` response as
`OffsetResult<T>` / `CursorResult<T>` plus a `400` problem response. Customize the name,
tag, and summary via the `configure` callback (`o.Name`, `o.Tag`, `o.Summary`).

## Embeddings (vector backend)

The vector backend embeds text via a pluggable `IEmbeddingProvider`. Model and dimensions
are always configurable — never hardcoded — and the dimensions drive the physical
`vector(N)` sidecar column. Pick a connector when enabling vector search:

```csharp
// OpenAI (or any OpenAI-compatible /v1/embeddings endpoint)
.UseVectorSearch(v => v.UseOpenAiEmbeddings(apiKey, "text-embedding-3-small", 1536))

// Ollama — runs locally, OpenAI-compatible /v1 (great for dev/CI; nomic-embed-text is 768d)
.UseVectorSearch(v => v.UseOllamaEmbeddings("http://localhost:11434"))

// Any Microsoft.Extensions.AI generator (native OllamaSharp, Azure, etc.)
.UseVectorSearch(v => v.UseEmbeddingGenerator(sp => myGenerator, "model-id", 768))

// Fully custom
.UseVectorSearch(v => v.UseEmbeddingProvider(sp => new MyProvider()))
```

`UseOpenAiEmbeddings` takes an optional `endpoint` (default `https://api.openai.com`) and
`sendDimensionsParam` (default `true`; set `false` for fixed-dimension models like
`nomic-embed-text` that don't accept the `dimensions` request field).

**Version transitions.** Stored vectors are tracked in a `ferret_vector_versions` registry
and live in version-stamped columns (`<group>_embedding_v1`). Changing the model or
dimensions requires re-running `engine.ReindexAsync<T>(session, "<group>")` — until you do,
vector search fails loud ("embedding model/dimensions changed; reindex required") rather
than silently searching incompatible vectors. v1 keeps a single active version per group;
zero-downtime backfill between versions is a planned addition.

## Compatibility

- .NET 10 SDK
- EF Core 10 (when using `Ferret.EntityFrameworkCore`)
- PostgreSQL 15, 16, 17 — CI matrix runs all three
- Requires `pg_trgm` extension; full-text needs no extra extension; vector backend needs `vector` (pgvector)
- `EntityFrameworkCore.ExtensibleMigrations` (sister repo) — required by `Ferret.Migrations` until that package stabilises on NuGet

## Design

See `docs/superpowers/specs/2026-04-25-ferret-decouple-design.md` for the full design spec, and `docs/superpowers/plans/2026-04-26-ferret-foundation.md` for the foundation plan.

## Migration from EntityFrameworkCore.PagedQuery

A `EntityFrameworkCore.PagedQuery.Compat` shim package re-exporting old types as `[Obsolete]` type-forwards is planned. Doc page covering the mechanical rename arrives with it.

## License

MIT.
