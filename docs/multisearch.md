# Multisearch

Search several entity types in one call and get back lightweight, grouped display
hits — built for type-ahead / cmd-K UIs. One database round-trip, top-k per entity
type, no paging.

## Shape

There are no hit DTOs: you project each entity into *itself*, selecting only the
fields you want back. Hits are instances of your entity type with just those
members populated — treat them as display data, never persist one.

```csharp
var result = await queryService.MultiSearchAsync(q => q
    .Search("jon")
    .PerEntityLimit(5)
    .Entity<User>(u => new User { Id = u.Id, Name = u.Name, Email = u.Email })
    .Entity<Product>(p => new Product { Id = p.Id, Name = p.Name, Sku = p.Sku },
        o => o.Filter(new FilterClause
        {
            Field = "Hidden", Operator = FilterOperator.Equals, Value = "false",
        })));

foreach (var group in result.Groups)          // registration order
{
    // group.EntityType — table name, e.g. "products"
    // group.Hits — best-match first; runtime type is the entity type
}

var users = result.HitsOf<User>();            // typed access, best-match first
```

- Hits are ordered best-match first within each group; the order *is* the ranking.
  There are no scores — backend-native scores aren't comparable across types
  anyway, and consumers render in published order.
- Want the key on a hit? Project it: `u => new User { Id = u.Id, … }`. Composite
  keys are just more projected members.
- `PerEntityLimit` caps each group independently (default 5). There is no overall
  ranking across types: groups are independent, so entities with different backends
  can share one call.
- Per-entity `Filter` clauses use the same `[Filterable]` fields and operators as
  single-entity queries. Filters apply after the per-arm top-k ranking, so a
  heavily filtered entity can return fewer than `PerEntityLimit` hits.
- The `Types` subset (builder `.Types("users")` or query property) narrows which
  registered entities run; an unknown name throws `ArgumentException` listing the
  valid set.

## Endpoint

`Ferret.AspNetCore` ships a mapper mirroring `MapFerret`:

```csharp
app.MapFerretMultiSearch("/api/search", o => o
    .WithLimits(defaultPerEntityLimit: 5, maxPerEntityLimit: 25)
    .Entity<User>(u => new User { Id = u.Id, Name = u.Name, Email = u.Email })
    .Entity<Product>(p => new Product { Id = p.Id, Name = p.Name, Sku = p.Sku }));
```

`GET /api/search?q=jon&types=users,products&limit=5` →

```json
{
  "groups": [
    { "entityType": "users",
      "hits": [ { "id": "…", "name": "Jon Snow", "email": "jon@wall.org" } ] },
    { "entityType": "products", "hits": [] }
  ]
}
```

The default response is `MultiSearchApiResponse` (`Produces` documents it). Only
the projected members go on the wire — unprojected entity properties are never
serialized, so a partially-populated entity never leaks default values into the
response. Hit keys follow the app's configured JSON property naming policy
(camelCase in a default web app, snake_case if you configure it), matching the
envelope. Endpoint projections must be member-inits (`u => new User { … }`) so
the field names are known at map time. Blank `q`, `limit` over the max, or an
unknown `types` name → 400 ProblemDetails.

Need a different envelope? `ShapeResponse` hands you the raw `MultiSearchResult`
(hits are partially-populated entities) and serializes whatever you return:

```csharp
app.MapFerretMultiSearch("/api/search", o => o
    .Entity<User>(u => new User { Id = u.Id, Name = u.Name })
    .ShapeResponse(r => new { users = r.HitsOf<User>().Select(u => new { u.Id, u.Name }) }));
```

## Execution model

Each entity becomes one ranked-arm statement (the same per-backend ranked-candidate
SQL hybrid search uses) joined to the base table for the projected columns, executed
together as a single `DbBatch` — one round-trip, one result set per group, native
column types throughout.

Hits are materialized by Ferret's own projection reader directly from that result
set, in one batch — not hydrated through EF Core or Dapper. They are plain,
partially-populated instances of your entity type: no EF change tracking, no
lazy-loading proxies, no identity resolution. Treat them as read-only display data;
callers who need a tracked entity should re-load it by key through their normal
EF/Dapper path.

## v1 limits

- Backends: trigram and fulltext arms only. A vector or hybrid-configured entity in
  a multisearch throws `NotSupportedException`. Single-backend-per-entity rules
  match normal search (`AsPrimary()` for multi-backend entities).
- No offset/cursor paging, no total counts, no scores, no `MatchInfo`, no
  `CandidateKeys`.
- Projections: member-init (or constructor call, core-only) over direct entity
  properties and constants — no method calls, no navigation properties.
