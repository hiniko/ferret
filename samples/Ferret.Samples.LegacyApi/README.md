# Ferret.Example.LegacyApi

Minimal ASP.NET Core API showing Ferret behind the **legacy wire-shape** compat layer
(`Ferret.Compat.LegacyApi`). The endpoint accepts the original backend's query parameters
(`page`, `page_size`, `search`, etc.) and returns the original response shape (`items`,
`count`, `total`, `match_info`) so existing clients keep working.

## Run

```bash
# from repo root
cd samples
docker compose up -d
cd Ferret.Example.LegacyApi
dotnet run
```

API listens on `http://localhost:5000` (or whatever `ASPNETCORE_URLS` says).
On first start the seeder runs `db.Database.Migrate()` and inserts 200 deterministic products.

## Example URLs

```
GET /products
GET /products?page=0&page_size=25
GET /products?search=widge
GET /products?filter=Category:eq:tools&sort=Name:asc
GET /products?include_match_info=true
GET /products?include_hidden=true
```

Response shape uses legacy field names: `items`, `page`, `count`, `total`, `match_info`.

## What this sample shows

- `[Searchable]`, `[Filterable]`, `[Sortable]` attribute discovery on a plain entity.
- ASP.NET Core MVC binding the legacy `page` / `page_size` / `search` query shape into
  `LegacyApiQuery` (from `Ferret.Compat.LegacyApi`).
- The `db.SearchOffsetAsync<T, TKey>` extension running the engine in offset mode, then
  `.ToLegacyResponse(p => p.Id)` reshaping the result for the legacy wire format.

## Notes

- Database state is shared with the other samples. Run one at a time.
