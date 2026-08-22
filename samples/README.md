# Ferret samples

Runnable example APIs. The first two demonstrate Ferret's URL-driven filter / sort /
search / page query model against the same schema, so diffing them shows exactly what
changes between offset and cursor pagination. The third shows adding Ferret to an
application that already exists.

| Project | What it shows |
| --- | --- |
| `Ferret.Samples.Basic` | Offset paging; `OffsetApiQuery` + `OffsetResult<T>` |
| `Ferret.Samples.Cursor` | Cursor paging; `CursorApiQuery` + `CursorResult<T>` |
| `Ferret.Samples.ExistingApi` | Ferret added to an existing DbContext, controller and DTO; the candidate-keys pattern; full-text search and a multisearch endpoint |

All three inject `IFerretQueryService` and call `SearchOffsetAsync` / `SearchCursorAsync` —
the blessed path. Each sample is self-contained: entity, DI wiring, controller and seeder
live in the same project so a reader can grok one app without crossing project boundaries.

## Run

```bash
docker compose up -d              # postgres on :5433
cd Ferret.Samples.Basic && dotnet run
# or
cd Ferret.Samples.Cursor && dotnet run
# or
cd Ferret.Samples.ExistingApi && dotnet run
```

`Basic` and `Cursor` share the same `products` table in the `ferret_samples` database —
run one at a time. The seeder is idempotent (skips when the table is already populated),
so you can switch between them without resetting Postgres.

`ExistingApi` uses its own `ferret_existing` database and does not collide with the other
two; it needs that database created first (see its README).

Override the connection string with `ConnectionStrings__Postgres` if port 5433 is already
taken on your machine.

See each project's README for example query URLs.

## Schema

`Basic` and `Cursor` seed a `products` table with 200 deterministic rows in five categories
(`tools`, `garden`, `kitchen`, `office`, `outdoor`). EF migrations — shared between those
two projects — create the table and the GiST `gist_trgm_ops` index on the `[Searchable]`
columns.

`ExistingApi` seeds its own `tickets` and `organisations` tables: 60 tickets across two
tenants, roughly 10% soft-deleted, so tenant scoping and soft-delete exclusion are
observable.
