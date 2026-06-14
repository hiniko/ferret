# Schema & migrations

Ferret **generates the schema it needs** — extensions, indexes, full-text sidecar tables,
sync functions/triggers, and pgvector columns/indexes — from your `[Searchable]` attributes.
You do not hand-maintain it (though you can; see the [manual reference](#manual-schema-reference)).

Schema generation ships in **`Ferret.Migrations`**, an EF Core migrations bridge built on
[`EntityFrameworkCore.ExtensibleMigrations`](https://github.com/hiniko/ExtensibleMigrations)
([NuGet](https://www.nuget.org/packages/EntityFrameworkCore.ExtensibleMigrations)).

> A standalone `Ferret.Schema` library + `dotnet ferret-schema` CLI for emitting DDL **without**
> EF Core is planned, for Dapper/BYO users who don't keep a `DbContext`.

## Install

```bash
dotnet add package Ferret.Migrations
```

## Wire it up

Add the annotation hook to `OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    // … your usual entity configuration …
    modelBuilder.UseFerretSearchableAnnotations(typeof(Product).Assembly);
}
```

Wire the design-time services — create `<YourProjectRoot>/MigrationsDesignTimeServices.cs`:

```csharp
using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

public sealed class MigrationsDesignTimeServices : IDesignTimeServices
{
    public void ConfigureDesignTimeServices(IServiceCollection services)
    {
        // The bridge scans loaded assemblies for [CustomMigrationHandler] types and registers
        // them — Ferret's handlers (trigram, full-text, vector) auto-wire here.
        new ExtensibleMigrationsDesignTimeServices().ConfigureDesignTimeServices(services);
    }
}
```

Then scaffold:

```bash
dotnet ef migrations add InitialFerretIndexes
```

This emits, per enabled backend: the required extension(s), trigram GiST `gist_trgm_ops`
indexes, full-text sidecar tables + GIN `tsvector` columns + sync function/trigger, and
pgvector `vector(N)` columns + HNSW indexes + the `ferret_vector_versions` registry.

> Prefer explicit handler registration over assembly auto-scan? Call
> `services.AddFerretMigrations()` instead of `ExtensibleMigrationsDesignTimeServices`.
> Don't call both — that double-registers each handler.

Note: the sidecar PK column reuses the source entity's key column name (e.g. `id`), not a
generic `entity_id`. `Ferret.Migrations` preserves this convention.

## Reindex modes

Full-text groups choose how the migration backfills (per group via
`[SearchGroup(..., Reindex = ...)]`, or globally via `FullTextOptions.DefaultReindex`):

- **`Inline`** (default) — the migration runs the backfill `INSERT … SELECT` in one
  transaction. Best for small tables and dev.
- **`Concurrent`** — the migration enqueues a row into `ferret_reindex_jobs`, drained by the
  reindex worker ([below](#reindex-worker)) — no long transaction during migration.
- **`Deferred`** — migration emits schema + trigger only; you trigger the backfill.

Vector groups are **explicit-only**: the migration creates the column/index/registry, and you
run `engine.ReindexAsync<T>(session, "<group>")` to populate embeddings (embeddings need an
out-of-process provider the migration can't call).

## Reindex worker

`Ferret.Hosting` provides a `BackgroundService` that drains `ferret_reindex_jobs` (advisory-lock
claim, keyset chunks, batch + throttle, resumable). Register it with
`AddFerretReindexHostedService`. Or run it once from the CLI:

```bash
dotnet ferret reindex --entity Product --group content
```

(`Ferret.Tools.Cli` — `dotnet tool`.)

## Manual schema reference

If you take full responsibility for schema, here are the shapes Ferret expects.

### Trigram

```sql
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE INDEX CONCURRENTLY products_name_gist_trgm ON products USING gist (name gist_trgm_ops);
CREATE INDEX CONCURRENTLY products_sku_gist_trgm  ON products USING gist (sku  gist_trgm_ops);
```

### Full-text sidecar

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

For pgvector, the sidecar carries version-stamped `vector(N)` columns + HNSW indexes; see
[vector-search.md](vector-search.md).
