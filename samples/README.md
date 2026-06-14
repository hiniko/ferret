# Ferret samples

Runnable example APIs that demonstrate Ferret's URL-driven filter / sort / search / page
query model. All samples target the same Postgres schema and the same domain entity, so
diffing them shows exactly what changes between the offset-mode, cursor-mode and
legacy-compat surfaces.

| Project | What it shows |
| --- | --- |
| `Ferret.Example.Basic` | Offset-paging endpoint; `OffsetApiQuery` + `OffsetResult<T>` |
| `Ferret.Example.Cursor` | Cursor-paging endpoint; `CursorApiQuery` + `CursorResult<T>` |
| `Ferret.Example.LegacyApi` | Backend wire-compat shape via `Ferret.Compat.LegacyApi` |

Each sample is self-contained: domain entity, DI wiring, controller, and seeder
all live in the same project so a reader can grok one app without crossing project
boundaries.

## Run

```bash
docker compose up -d              # postgres on :5433
# Pick any one of the three samples
cd Ferret.Example.Basic && dotnet run
# or
cd Ferret.Example.Cursor && dotnet run
# or
cd Ferret.Example.LegacyApi && dotnet run
```

Only run one sample at a time — they all share the same `products` table. The seeder is
idempotent (skips when the table is already populated), so you can switch between samples
without resetting Postgres.

See each project's README for example query URLs.

## Schema

The samples seed a `products` table with 200 deterministic rows in five categories
(`tools`, `garden`, `kitchen`, `office`, `outdoor`). EF migrations (shared between the
three projects) create the table and the GiST `gist_trgm_ops` index on the
`[Searchable]` column.
