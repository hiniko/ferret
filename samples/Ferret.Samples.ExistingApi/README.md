# Existing API sample

Ferret added to an application that already exists — the case most integrations
actually face. The Basic and Cursor samples start from an empty project; this one
starts from an API with its own base class, its own DbContext conventions, its own
response envelope, and its own filtering rules, then adds search without changing any
of them.

Read alongside [docs/integration.md](../../docs/integration.md).

## What was already here

| | |
|---|---|
| `Domain/AuditedEntity.cs` | Pre-existing base class (`Id`, `CreatedAt`, `IsDeleted`) |
| `Domain/Ticket.cs` | Entity with a `[Redacted]` attribute from the app's own system |
| `Domain/TicketDto.cs` | `TicketDto` + `ApiPage<T>` — the wire contract, fixed |
| `AppDbContext.cs` | Explicit column mappings and a global soft-delete query filter |
| `TicketRepository.cs` | Repository with tenant scoping |
| `Controllers/TicketsController.cs` | Route, parameters and response shape, fixed |

## What Ferret needed

Three things. Everything else is unchanged.

1. **Attributes on the entity** — `[SearchableEntity]`, `[Searchable]`, `[Filterable]`,
   `[Sortable]`. No new base class, no interface. `Ticket` still extends the app's own
   `AuditedEntity`, and the pre-existing `[Redacted]` attribute sits beside Ferret's
   without conflict. `Ticket.Subject` and `Ticket.Body` are full-text (`Backend =
   SearchBackend.FullText`, shared `content` group) — language-aware relevance ranking
   suits ticket prose better than trigram fuzzy matching; `Organisation.Name` stays
   trigram (the default), so the sample demonstrates both backends side by side.

2. **One line in `OnModelCreating`** — `modelBuilder.UseFerretSearchableAnnotations(...)`,
   after all the existing configuration.

3. **Three calls in `Program.cs`** — `AddFerret(...)` (with both `UseTrigramSearch()` and
   `UseFullTextSearch()`) and `AddFerretEntityFrameworkCore<AppDbContext>()`.

Note this sample does *not* call `AddFerretAspNetCore()` or `AddFerretMvcBinding()`. It
keeps its own `?q=`/`?page=`/`?perPage=` parameters rather than adopting Ferret's
query-string grammar — the realistic choice when an API contract already exists. See
[docs/aspnetcore.md](../../docs/aspnetcore.md) if you do want the binding. The multisearch
endpoint (`/search`, below) is the exception — it's mapped directly with
`MapFerretMultiSearch`, since multisearch has no repository-level equivalent to wrap.

## Server-enforced filters

`TicketRepository.Search` is the part worth reading. Two rules must hold on every query, and
neither is the caller's to decide:

- **tenant scoping** — a request must only ever see its own organisation's tickets
- **soft delete** — deleted rows must never appear

Both are expressed as *authoritative* filters:

```csharp
AuthoritativeFilter =
[
    Filters.Eq(nameof(Ticket.OrganisationId), organisationId),
    Filters.Eq(nameof(Ticket.IsDeleted), false),
],
```

Two things make this different from an ordinary `Filter`:

1. **It is compiled into the search query.** Postgres applies it while ranking — nothing is
   pre-loaded, and no list of ids crosses the wire. The work stays where the indexes are.
2. **It is authoritative.** A caller clause on the same field is *replaced*, not combined, so a
   request cannot widen the boundary by filtering on `OrganisationId` itself.

The properties must be `[Filterable]` for this to work — see `Ticket.OrganisationId` and
`AuditedEntity.IsDeleted`.

After ranking, the repository re-hydrates with the `Include`s the endpoint needs and **restores
Ferret's ordering** — `WHERE id IN (...)` returns database order, not relevance order. That
reorder is easy to forget and produces subtly wrong results rather than errors.

### When you genuinely cannot express a rule as a filter

`PagedQuery.CandidateKeys` takes a pre-computed key list for predicates that have no
`FilterClause` equivalent. It materialises those keys in your application, so treat it as an
escape hatch rather than a default — see [docs/advanced.md](../../docs/advanced.md).

## Run

```bash
docker compose -f ../docker-compose.yml up -d       # postgres on :5433
createdb -h localhost -p 5433 -U postgres ferret_existing   # or via psql
dotnet run
```

Uses its own `ferret_existing` database, so it will not collide with the Basic and Cursor
samples (which share `products`). Override the connection with
`ConnectionStrings__Postgres` if port 5433 is taken.

The seeder migrates, then inserts 2 organisations and 60 tickets — about 10% soft-deleted,
split across both tenants, deterministic.

## Try it

```bash
ORG_A=aaaaaaaa-0000-0000-0000-000000000001
ORG_B=bbbbbbbb-0000-0000-0000-000000000002

# Plain listing — the path that existed before Ferret
curl "localhost:5199/tickets?organisationId=$ORG_A&perPage=3"

# Search — full-text on Subject/Body, tenant + soft-delete enforced inside the query
curl "localhost:5199/tickets?organisationId=$ORG_A&q=printer"

# Tenant scoping: same term, different org, different count
curl "localhost:5199/tickets?organisationId=$ORG_B&q=printer"

# Multisearch — tickets (full-text) and organisations (trigram) in one call, no tenant
# scoping (multisearch v1 has no filters/CandidateKeys — see docs/multisearch.md)
curl "localhost:5199/search?q=printer"
curl "localhost:5199/search?q=northwind"
```

The `/tickets` response is the app's own `{ data, total, page, perPage }` envelope in every
case — Ferret's `OffsetResult<T>` is projected into it and never reaches the wire.
`/search` returns Ferret's own `{ groups: [{ entityType, hits }] }` shape directly (see
[docs/multisearch.md](../../docs/multisearch.md)); it has no pre-existing contract to
preserve, so `MapFerretMultiSearch`'s default response ships as-is.

## Migration

`Migrations/*_InitialWithFerretIndexes.cs` and `Migrations/*_AddFullTextSearch.cs` were
generated by `dotnet ef migrations add`. The `UseFerretSearchableAnnotations` line means
the Ferret index DDL is scaffolded automatically alongside the ordinary table DDL — a GiST
trigram index for `Organisation.Name`:

```sql
CREATE EXTENSION IF NOT EXISTS "pg_trgm";
CREATE INDEX CONCURRENTLY IF NOT EXISTS "ix_organisations_name_gist_trgm"
    ON "organisations" USING gist (("name"::text) gist_trgm_ops);
```

and a `tickets_search` sidecar table with a GIN-indexed `tsvector` column plus a trigger
that keeps it in sync with `Subject`/`Body` on insert/update, for the full-text group:

```sql
CREATE TABLE IF NOT EXISTS "tickets_search" (
    "id" uuid PRIMARY KEY REFERENCES "tickets" ("id") ON DELETE CASCADE,
    "updated_at" timestamptz NOT NULL DEFAULT now()
);
ALTER TABLE "tickets_search" ADD COLUMN IF NOT EXISTS "content_tsv" tsvector;
CREATE INDEX IF NOT EXISTS "ix_tickets_search_content_tsv_gin"
    ON "tickets_search" USING gin ("content_tsv");
```

The trigram GiST index runs `CONCURRENTLY` so it applies to a populated table without
locking writes — EF warns that a concurrent index cannot run inside a transaction, expected,
and why that operation suppresses the transaction. The sidecar's GIN index does not need
`CONCURRENTLY`: `tickets_search` is created and indexed empty, then backfilled by the same
migration, so there's no populated table to lock against.
