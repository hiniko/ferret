# Ferret.Example.Basic

Minimal ASP.NET Core API showing Ferret with EF Core and `Ferret.Migrations`,
using the offset-based `OffsetApiQuery` binder.

## Run

```bash
# from repo root
cd samples
docker compose up -d
cd Ferret.Example.Basic
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

- The library extension `dbContext.SearchAsync<>` resolves `IFerretEngine` from EF's
  internal service provider, which doesn't see services registered via `AddFerret(...)`
  on the application provider. This sample injects `IFerretEngine` + `ISqlDialect` into
  the controller and constructs `EntityFrameworkSession` directly.
- Database state is shared with the Cursor and LegacyApi samples. Run one at a time.
