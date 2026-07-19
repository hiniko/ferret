# Multisearch

Search several entity types in one call and get back lightweight, grouped display
hits — built for type-ahead / cmd-K UIs. One database round-trip, top-k per entity
type, no paging.

## Shape

You define one hit class per entity, subclassing `MultiSearchHit`. The engine
populates the base members (`EntityType`, `Key`, `Score`); your projection fills the
rest. Projections are plain expressions — property accesses and constants only,
translated straight to SQL columns.

```csharp
public sealed class UserHit : MultiSearchHit
{
    public string Name { get; set; } = "";
    public string? Email { get; set; }
}

public sealed class ProductHit : MultiSearchHit
{
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
}

var result = await queryService.MultiSearchAsync(q => q
    .Search("jon")
    .PerEntityLimit(5)
    .Entity<User, UserHit>(u => new UserHit { Name = u.Name, Email = u.Email })
    .Entity<Product, ProductHit>(p => new ProductHit { Name = p.Name, Sku = p.Sku },
        o => o.Filter(new FilterClause
        {
            Field = "Hidden", Operator = FilterOperator.Equals, Value = "false",
        })));

foreach (var group in result.Groups)          // registration order
{
    // group.EntityType — table name, e.g. "products"
    // group.Hits — best-match first, runtime type is your concrete hit class
}
```

- `PerEntityLimit` caps each group independently (default 5). There is no overall
  ranking across types: groups are independent, so entities with different backends
  can share one call and scores never compete across groups.
- `Score` is backend-native: trigram scores are distances (0.0 = perfect match,
  lower is better); fulltext scores are `ts_rank_cd` values (higher is better).
  Order within a group is always correct regardless.
- `Key` is the string-encoded primary key; composite key parts are joined with
  U+001F.
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
    .Entity<User, UserHit>(u => new UserHit { Name = u.Name, Email = u.Email })
    .Entity<Product, ProductHit>(p => new ProductHit { Name = p.Name, Sku = p.Sku }));
```

`GET /api/search?q=jon&types=users,products&limit=5` →

```json
{
  "groups": [
    { "entityType": "users",
      "hits": [ { "entityType": "users", "key": "…", "score": 0.15,
                  "name": "Jon Snow", "email": "jon@wall.org" } ] },
    { "entityType": "products", "hits": [] }
  ]
}
```

Hits serialize by their runtime type — the concrete hit properties appear directly,
`entityType` is the discriminator, no synthetic `$type` is emitted. Blank `q`,
`limit` over the max, or an unknown `types` name → 400 ProblemDetails.

## Execution model

Each entity becomes one ranked-arm statement (the same per-backend ranked-candidate
SQL hybrid search uses, plus the projected display columns), executed together as a
single `DbBatch` — one round-trip, one result set per group, native column types
throughout.

## v1 limits

- Backends: trigram and fulltext arms only. A vector or hybrid-configured entity in
  a multisearch throws `NotSupportedException`. Single-backend-per-entity rules
  match normal search (`AsPrimary()` for multi-backend entities).
- No offset/cursor paging, no total counts, no `MatchInfo`, no `CandidateKeys`.
- Projections: `new THit(...)` or member-init over direct entity properties and
  constants — no method calls, no navigation properties, no assigning the
  engine-owned base members.
