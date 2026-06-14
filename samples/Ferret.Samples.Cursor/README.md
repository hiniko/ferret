# Ferret.Example.Cursor

Minimal ASP.NET Core API showing Ferret in **cursor-pagination** mode with EF Core and
`Ferret.Migrations`.

## Run

```bash
# from repo root
cd samples
docker compose up -d
cd Ferret.Example.Cursor
dotnet run
```

API listens on `http://localhost:5000` (or whatever `ASPNETCORE_URLS` says).
On first start the seeder runs `db.Database.Migrate()` and inserts 200 deterministic products.

## Example URLs

Field names in `filter` and `sort` are CLR property names (PascalCase) per the v1 binder:

```
GET /products
GET /products?limit=25
GET /products?after=<token>&limit=25
GET /products?q=widge
GET /products?sort=Price:asc&limit=10
GET /products?before=<token>&limit=25  (backward navigation)
```

After fetching the first page, use the `nextCursor` value from the response as
`?after=<token>` on the next request.

## What this sample shows

- `[Searchable]`, `[Filterable]`, `[Sortable]` attribute discovery on a plain entity.
- `Ferret.Migrations` auto-generating `CREATE EXTENSION pg_trgm` + GiST `gist_trgm_ops`
  indexes from `[Searchable]` annotations during `dotnet ef migrations add`.
- ASP.NET Core MVC binding the compact `filter=field:op:value&sort=field:dir` query shape
  into `CursorApiQuery`.
- The `db.SearchCursorAsync<T, TKey>` extension running the engine in cursor mode.

## Notes

- Database state is shared with the other samples. Run one at a time.
