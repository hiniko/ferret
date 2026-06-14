# ASP.NET Core

Two ways to expose Ferret over HTTP: bind the compact query types in your own controllers, or
scaffold a full endpoint with `MapFerret`.

## Query types in your controllers

Non-generic `OffsetApiQuery` / `CursorApiQuery` bind compact query strings and map to a
`PagedQuery<T, TKey>` at the controller boundary, so OpenAPI shows one shape per endpoint.

### Offset

```
GET /products?page=0&limit=25&q=widge&filter=Name:eq:Blue&sort=Name:asc
```

```csharp
[HttpGet, Route("products")]
public Task<OffsetResult<Product>> Get(
    [FromQuery] OffsetApiQuery q, PaginationDefaultsResolver resolver,
    IServiceProvider sp, CancellationToken ct)
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
    [FromQuery] CursorApiQuery q, PaginationDefaultsResolver resolver,
    IServiceProvider sp, CancellationToken ct)
{
    var defaults = resolver.Resolve(HttpContext);
    return db.SearchCursorAsync<Product, Guid>(sp, q.ToPagedQuery<Product, Guid>(defaults), ct);
}
```

Field names in `filter` / `sort` are CLR property names (PascalCase). Filter operators: `eq`,
`neq`, `contains`, `gt`, `gte`, `lt`, `lte`, `in` (comma-separated list, e.g.
`filter=Category:in:tools,garden`).

### Pagination defaults

```csharp
services.AddFerret(opts => opts.WithPaginationDefaults(defaultLimit: 25, maxLimit: 100));
services.AddFerretAspNetCore();   // registers PaginationDefaultsResolver
```

Override per endpoint with `[PaginationLimits(Default = 50, Max = 200)]`.

## MapFerret — one-line endpoint

`MapFerret<T, TKey>` scaffolds a complete JSON query endpoint — binding, validation, paging,
and OpenAPI metadata included.

```csharp
app.MapFerret<Product, Guid>("/api/products");
```

### Prerequisites

```csharp
builder.Services.AddFerret(opts => opts
    .ScanAssembly(typeof(Product).Assembly).UsePostgres().UseTrigramSearch());
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddFerretEntityFrameworkQueryService<AppDbContext>(); // IFerretQueryService
builder.Services.AddFerretAspNetCore();                                // PaginationDefaultsResolver
```

The endpoint resolves `IFerretQueryService` from DI, so any backend (EF Core, Dapper, or the
in-process core service) works without changing the route.

### Query-string grammar

| Parameter    | Applies to | Meaning |
|--------------|------------|---------|
| `q`          | both       | Full-text / trigram search term |
| `fields`     | both       | Comma-separated property names to search |
| `match_info` | both       | `true` to include search match metadata |
| `filter`     | both       | `Field:op:value`, repeatable. Ops: `eq`, `neq`, `contains`, `gt`, `gte`, `lt`, `lte`, `in` |
| `sort`       | both       | `Field` or `Field:asc` / `Field:desc`, repeatable |
| `limit`      | both       | Page size; `400` if above the configured max |
| `page`       | offset     | 1-based page number |
| `count`      | offset     | `false` to skip the total-count query |
| `after`      | cursor     | Token to page forward from |
| `before`     | cursor     | Token to page backward from |

### Responses

Offset → `OffsetResult<T>` (`items`, `limit`, `page`, `totalCount`, `hasMore`, `hasPrev`).
Cursor → `CursorResult<T>` (`items`, `limit`, `nextCursor`, `prevCursor`, `hasMore`,
`hasPrev`). Feed `nextCursor` back as `after`, `prevCursor` as `before`. An invalid or tampered
token returns `400 Problem`.

### Sibling routes & OpenAPI

Pick the mode per route with the `configure` overload:

```csharp
app.MapFerret<Product, Guid>("/api/products");                       // offset (default)
app.MapFerret<Product, Guid>("/api/products/stream",
    o => o.Pagination = FerretEndpointPaginationMode.Cursor);        // cursor sibling
```

With `AddOpenApi()` registered, `MapFerret` documents every parameter for the chosen mode and
the `200`/`400` responses. Customize via the `configure` callback (`o.Name`, `o.Tag`,
`o.Summary`).

## Legacy wire shape

Migrating a service whose clients depend on the original PagedQuery parameter names (`page`,
`page_size`, `search`, `search_fields`, `include_match_info`, `include_hidden`) and response
JSON (`items`, `page`, `count`, `total`, `match_info`)? Reference `Ferret.Compat.LegacyApi`,
bind `LegacyApiQuery`, and return `LegacyPagedResponse<T>` via
`result.ToLegacyResponse(p => p.Id)`. See `samples/Ferret.Example.LegacyApi`.
