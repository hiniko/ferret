# Ferret.Example.Basic

Minimal ASP.NET Core API showing Ferret with EF Core and `Ferret.Migrations`,
using the offset-based `OffsetApiQuery` binder.

## Run

```bash
# from repo root
cd samples
docker compose up -d
cd Ferret.Samples.Basic
dotnet run
```

API listens on `http://localhost:5000` (or whatever `ASPNETCORE_URLS` says).
On first start the seeder runs `db.Database.Migrate()` and inserts 200 deterministic products.

## Example URLs

Field names in `filter` and `sort` are CLR property names (PascalCase) per the v1 binder.
Supported filter operators: `eq`, `contains`, `gt`, `lt`.

```
GET /products
GET /products?limit=25&page=0
GET /products?q=widge
GET /products?filter=Category:eq:tools
GET /products?filter=Price:lt:50&sort=Name:asc
GET /products?filter=Name:contains:widget&sort=CreatedAt:desc&page=1&limit=10
```

## What this sample shows

- `[Searchable]`, `[Filterable]`, `[Sortable]` attribute discovery on a plain entity.
- `Ferret.Migrations` auto-generating `CREATE EXTENSION pg_trgm` + GiST `gist_trgm_ops`
  indexes from `[Searchable]` annotations during `dotnet ef migrations add`.
- ASP.NET Core MVC binding the compact `filter=field:op:value&sort=field:dir` query shape
  into `OffsetApiQuery`.
- An `EntityFrameworkSession` opened per request, executing search through the engine.

## Adding a new searchable field

1. Add the property + attribute on `Product.cs`.
2. Add an `HasColumnName` in `AppDbContext.OnModelCreating` (snake_case to match Ferret's
   default naming strategy).
3. `dotnet ef migrations add <Name>` — the new GiST index is scaffolded automatically.

## Notes

- The controller injects `IFerretQueryService` and calls `SearchOffsetAsync` — the
  blessed path. `AddFerretEntityFrameworkCore<AppDbContext>()` registers it; nothing
  needs to construct a session by hand. See `docs/advanced.md` for the lower-level
  engine + session API.
- Database state is shared with the Cursor sample. Run one at a time.
