# ASP.NET Core

Two ways to expose Ferret over HTTP: bind the compact query types in your own controllers, or
scaffold a full endpoint with `MapFerret`.

## Query types in your controllers

Non-generic `OffsetApiQuery` / `CursorApiQuery` bind compact query strings and map to a
`PagedQuery<T, TKey>` at the controller boundary, so OpenAPI shows one shape per endpoint.

Register both alongside `AddControllers()` — they are independent registrations, neither
calls the other: `AddFerretAspNetCore()` registers `PaginationDefaultsResolver` (used below),
`AddFerretMvcBinding()` registers the model-binder provider for `[FromQuery]` on these types.

```csharp
builder.Services.AddFerretAspNetCore();
builder.Services.AddFerretMvcBinding();  // required for [FromQuery] on these types
```

Without it, MVC binds these as complex objects prefixed by the **action parameter name**.
An action written as `Get([FromQuery] OffsetApiQuery q, …)` makes MVC look for `q.Q`,
`q.limit`, … the moment a request carries `?q=`, so every property silently takes its
default — a search then returns an unfiltered page rather than failing loudly.

### Offset

```
GET /products?page=0&limit=25&q=widge&filter=Name:eq:Blue&sort=Name:asc
```

```csharp
public sealed class ProductsController(IFerretQueryService search) : ControllerBase
{
    [HttpGet, Route("products")]
    public Task<OffsetResult<Product>> Get(
        [FromQuery] OffsetApiQuery q, PaginationDefaultsResolver resolver, CancellationToken ct)
    {
        var defaults = resolver.Resolve(HttpContext);
        return search.SearchOffsetAsync<Product, Guid>(q.ToPagedQuery<Product, Guid>(defaults), ct);
    }
}
```

### Cursor

```
GET /products?after=<token>&limit=25&q=widge&sort=Name:asc
```

```csharp
[HttpGet, Route("products/stream")]
public Task<CursorResult<Product>> Stream(
    [FromQuery] CursorApiQuery q, PaginationDefaultsResolver resolver, CancellationToken ct)
{
    var defaults = resolver.Resolve(HttpContext);
    return search.SearchCursorAsync<Product, Guid>(q.ToPagedQuery<Product, Guid>(defaults), ct);
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
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddFerret(opts => opts
    .ScanAssembly(typeof(Product).Assembly).UseTrigramSearch());
builder.Services.AddFerretEntityFrameworkCore<AppDbContext>(); // IFerretQueryService
builder.Services.AddFerretAspNetCore();                        // PaginationDefaultsResolver
```

The endpoint resolves `IFerretQueryService` from DI. `AddFerret` alone does not register one —
an `IFerretSession` owns a connection and is built per call, so only an adapter that knows where
connections come from can supply the service. Today that adapter is EF Core
(`AddFerretEntityFrameworkCore<TContext>()`).

Dapper consumers drive `IFerretEngine` directly with a `DapperSession` they construct themselves
(see [setup.md](setup.md)) and do not use `MapFerret`. To put Dapper behind these endpoints you
would add an `IFerretQueryService` that builds a `DapperSession` per call, mirroring
`EntityFrameworkQueryService<TContext>`.

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

## Mapping into an existing response DTO

If your API contract is fixed and can't return `OffsetResult<T>` / `CursorResult<T>` directly,
call `IFerretQueryService` yourself and project the result into your own envelope:

```csharp
var result = await search.SearchOffsetAsync<Product, Guid>(query, ct);
return new ProductListResponse
{
    Products = result.Items.Select(ProductDto.FromEntity).ToList(),
    Total = result.TotalCount,
    HasMore = result.HasMore,
};
```

`OffsetResult<T>` exposes `Items`, `Limit`, `Page`, `TotalCount`, `HasMore`, `HasPrev`, and
`MatchInfo`; `CursorResult<T>` swaps `Page`/`TotalCount` for `NextCursor`/`PrevCursor`. Neither
`MapFerret` nor the query types are required for this — `OffsetApiQuery`/`CursorApiQuery` still
save you writing the query-string parsing in `### Query-string grammar` above, even if the
response shape is your own.

