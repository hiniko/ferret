# Adding Ferret to an existing application

The [setup guide](setup.md) starts from an empty project. This one starts from an
application you already have: an existing `DbContext` with real entities, existing
controllers with response shapes you cannot change, existing filtering rules, and
possibly an existing search implementation you want to replace.

Nothing here requires touching your domain model beyond adding attributes.

## What you actually have to change

| Your existing code | Change required |
|---|---|
| `DbContext` | One line in `OnModelCreating` |
| Entity classes | Attributes only — no base class, no interface |
| DI registration | Three calls in `Program.cs` |
| Controllers / DTOs | Nothing forced; map results yourself |
| Existing search code | Can run side by side during migration |

Ferret does not require your entities to inherit anything. The only constraints are
`where T : class` and `where TKey : notnull`. `ISearchableEntity<TKey>` exists but is
optional.

## 1. Annotate existing entities

Add attributes to the entity you want searchable. Existing attributes from your own
systems are untouched — Ferret only reads its own:

```csharp
[SearchableEntity]
public class User : CrudEntity          // your existing base class, unchanged
{
    public Guid Id { get; set; }

    [Searchable] [Pii] public string? GivenName { get; set; }   // [Pii] is yours
    [Searchable] [Pii] public string? FamilyName { get; set; }
    [Searchable] [Pii] public string Email { get; set; } = null!;

    [Filterable(Operators = [FilterOperator.Equals])] public bool IsDeleted { get; set; }
    [Sortable] public DateTimeOffset CreatedAt { get; set; }
}
```

`[SearchableEntity]` assumes a key property named `Id`. If yours differs, or the entity
has a composite key:

```csharp
[SearchableEntity(KeyProperty = "UserId")]
[SearchableEntity(KeyProperties = ["TenantId", "Id"])]   // composite
```

With the EF adapter registered you can usually omit both — Ferret reads the primary key
from the EF model, including composite keys.

### Entities in a different assembly from your DbContext

Common in layered solutions. Scan both:

```csharp
services.AddFerret(o => o
    .ScanAssembly(typeof(User).Assembly)        // where entities live
    .ScanAssembly(typeof(AppDbContext).Assembly));
```

`ScanAssembly` is chainable and additive — call it once per assembly.

## 2. Register against your existing DbContext

Add to your existing `Program.cs`, after your existing `AddDbContext`:

```csharp
services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));  // yours, unchanged

services.AddFerret(o => o
    .ScanAssembly(typeof(User).Assembly)
    .UseTrigramSearch(t => t.MinimumSimilarity = 0.35)
    .WithPaginationDefaults(defaultLimit: 25, maxLimit: 100));

services.AddFerretEntityFrameworkCore<AppDbContext>();
```

If your database uses snake_case columns (`given_name`), that is Ferret's default. For
identity naming, or a custom convention:

```csharp
services.AddFerret(o => o.UseNamingStrategy<IdentityNamingStrategy>());
```

Getting this wrong produces empty results rather than errors — the generated SQL
references columns that do not exist in the shape Ferret expects. Verify against one
entity before rolling out.

## 3. Wire the model for migrations

One line in your existing `OnModelCreating`, alongside everything already there:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // ... your existing entity configuration, unchanged ...

    modelBuilder.UseFerretSearchableAnnotations(typeof(User).Assembly);
}
```

Then `dotnet ef migrations add AddSearchIndexes` scaffolds the index DDL. See
[migrations.md](migrations.md) for design-time setup and reindex modes.

This uses default backend options. If you configure non-default ones (e.g.
`UseFullTextSearch(ft => ft.DefaultConfig = "spanish")`), pass the service provider
instead — `UseFerretSearchableAnnotations(services, typeof(User).Assembly)` — so the DDL
matches your queries. See [migrations.md](migrations.md#wiring).

Generated indexes are `CREATE INDEX CONCURRENTLY` with transaction suppression, so they
apply to populated tables without locking writes.

## 4. Query from your existing service or controller

Inject `IFerretQueryService`. It does not require a `DbContext` parameter, an
`IServiceProvider`, or any change to your controller's return type:

```csharp
public sealed class UsersController(IFerretQueryService search) : ControllerBase
{
    [HttpGet]
    public async Task<UserListResponse> Get(string? q, int page = 0, CancellationToken ct = default)
    {
        var result = await search.SearchOffsetAsync<User, Guid>(new PagedQuery<User, Guid>
        {
            Search = q,
            Page = page,
            Limit = 25,
            RequestTotalCount = true,
        }, ct);

        // Map to YOUR existing response shape — Ferret does not impose one.
        return new UserListResponse(
            Items: result.Items.Select(UserDto.From).ToList(),
            Total: result.TotalCount,
            Page: result.Page);
    }
}
```

`OffsetResult<T>` carries `Items`, `TotalCount`, `Page`, `Limit`, `HasMore`, `HasPrev`.
Project it into whatever envelope your API already returns.

## 5. Keep your existing filtering rules — the candidate-keys pattern

This is the pattern most existing applications need, and it is worth understanding before
you wire anything up.

Real repositories have filtering that is not expressible as a `FilterClause`: soft-delete
predicates, tenant scoping, permission checks, EF global query filters, joins across
`Include`s. You cannot express those to Ferret, and you should not have to.

Also: projecting entities with `Include`s directly through search duplicates rows, because
the join fans out.

The fix is to let each side do what it is good at — **EF decides which rows are eligible,
Ferret ranks them**:

```csharp
public async Task<Page<User>> SearchUsers(string term, int page, CancellationToken ct)
{
    // 1. YOUR existing rules, in plain EF. No Includes here — keys only.
    var candidateIds = await _db.Users
        .Where(u => !u.IsDeleted)
        .Where(u => u.OrganisationId == _currentOrg)
        .Select(u => u.Id)
        .ToListAsync(ct);

    // 2. Ferret ranks only those rows.
    var ranked = await _search.SearchOffsetAsync<User, Guid>(new PagedQuery<User, Guid>
    {
        Search = term,
        CandidateKeys = candidateIds,
        Limit = 25,
        Page = page,
        RequestTotalCount = true,
    }, ct);

    // 3. Re-hydrate with YOUR Includes, then restore Ferret's ranking.
    var ids = ranked.Items.Select(u => u.Id).ToList();
    var hydrated = await _db.Users
        .Include(u => u.Organisation)
        .Where(u => ids.Contains(u.Id))
        .ToListAsync(ct);

    var byId = hydrated.ToDictionary(u => u.Id);
    var ordered = ids.Select(id => byId[id]).ToList();   // rank order, not DB order

    return new Page<User>(ordered, ranked.TotalCount, page);
}
```

Notes on this pattern:

- **Step 3 must reorder.** `WHERE id IN (...)` returns rows in whatever order Postgres
  chooses. Ferret's ranking is in `ranked.Items`; the dictionary lookup restores it.
- **An empty `CandidateKeys` list short-circuits** to an empty result — it does not mean
  "no restriction". Null means no restriction.
- `CandidateKeys` intersects with any `Filter` clauses you also pass.
- It is ignored when `Search` is not set.
- For very large candidate sets, prefer expressing what you can as `[Filterable]` clauses
  and reserve this for predicates that genuinely cannot be expressed.

When your filtering *is* expressible declaratively, skip all this and use `[Filterable]`
with `FilterClause` — see [aspnetcore.md](aspnetcore.md) for the query-string grammar.

## 6. Running alongside an existing search implementation

You do not have to cut over in one commit.

1. **Annotate and register Ferret** while your existing search stays in place. Adding
   attributes and DI registration changes no behaviour on its own.
2. **Add the index migration** and let it apply. Still no behaviour change.
3. **Write a parity test** that runs the same query through both paths and asserts the
   same rows come back:

   ```csharp
   [Fact]
   public async Task Ferret_matches_legacy_for_representative_queries()
   {
       foreach (var term in new[] { "exact", "typo", "@example.com", "prefix" })
       {
           var legacy = await _legacy.Search(term);
           var ferret = await _search.SearchOffsetAsync<User, Guid>(
               new PagedQuery<User, Guid> { Search = term, Limit = 25 });

           ferret.Items.Select(u => u.Id).Should().Equal(legacy.Select(u => u.Id));
       }
   }
   ```

   Pin the behaviours you actually depend on: exact match ranks first, typo tolerance,
   substring/fragment match, soft-deleted rows excluded, empty result for no match.
   If your existing engine used `pg_trgm`, match its similarity threshold —
   `UseTrigramSearch(t => t.MinimumSimilarity = 0.35)` — or results will differ at the
   margins.
4. **Switch one endpoint**, watch it, then roll out.
5. **Delete the old implementation**, keeping the parity test as a golden test of the
   behaviour you promised callers.

## Testing: DbContexts built outside DI

If your test fixtures construct a context directly — `new AppDbContext(options)` rather
than resolving it from the container — Ferret's key-override source cannot read the EF
model, because it looks for a registered context.

Point the options at a service provider that has Ferret registered:

```csharp
private static readonly IServiceProvider SearchServices = BuildSearchServices();

static IServiceProvider BuildSearchServices()
{
    var services = new ServiceCollection();
    // Placeholder connection string is fine — this provider never opens a connection.
    services.AddDbContext<AppDbContext>(o => o.UseNpgsql("Host=localhost;Database=placeholder"));
    services.AddFerret(o => o.ScanAssembly(typeof(User).Assembly).UseTrigramSearch());
    services.AddFerretEntityFrameworkCore<AppDbContext>();
    return services.BuildServiceProvider();
}

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(container.ConnectionString)
    .UseApplicationServiceProvider(SearchServices)   // <-- the fix
    .Options;
```

Build it once and share it; it is only there so the EF model can be read.

## Where to go next

- [advanced.md](advanced.md) — the lower-level `IFerretEngine` + `IFerretSession` path,
  custom hydration, and non-EF integration.
- [migrations.md](migrations.md) — reindex modes and backfilling populated tables.
- [aspnetcore.md](aspnetcore.md) — query-string binding if you want URL-driven
  filter/sort/page without hand-rolling it.
