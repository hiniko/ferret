# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Ferret is pre-1.0 — see `VERSIONING.md` for what that means for breaking changes.

## [Unreleased]

### Changed

- **Selectivity-based routing for filtered vector search** (default behavior change):
  filtered vector queries now choose between an index (HNSW) path and an exact-scan path
  based on estimated filter selectivity, instead of always using the index path. New
  `VectorOptions` knobs: `VectorFilterStrategy` (`Auto` / `AlwaysExact` / `AlwaysIndex`),
  `ExactScanThreshold`, `MaxExactScanRows`, and `HnswIterativeScan`. See
  `docs/vector-filtering.md`.
- SQL-building layer (`PagedSqlBuilder`, `SqlFragment`, `EntityMetadata`, `PostgresDialect`)
  is now `internal` (breaking, pre-1.0). These types had no downstream consumers and were
  shipped public by accident.

- Dependency updates: `Microsoft.Extensions.AI.Abstractions` minimum raised 9.7 → 10.11
  (consumers must be on 10.11 or later); EF Core / Npgsql to 10.0.13 / 10.0.3 and
  `Microsoft.Extensions.*` to 10.0.3.
- Releases are now published via NuGet trusted publishing, with GitHub build provenance
  attestations; the attested `.nupkg` files are attached to each GitHub release. See
  `SECURITY.md`.

### Fixed

- Vector offset-paging now returns a true `TotalCount`/`HasMore` instead of undercounting
  against the already-LIMIT-bounded KNN subquery. Behavioral note: with a true total count,
  offset-paged **filtered** vector search no longer stays on the pure-HNSW plan — the full
  candidate set must be evaluated to produce the count — so `AlwaysIndex` and `AlwaysExact`
  converge on that path. Cursor paging is unaffected and keeps pure-HNSW characteristics; it
  remains the recommended pagination mode for vector search. See `docs/vector-filtering.md`.
- Re-entrant model building via `UseFerretSearchableAnnotations` inside `OnModelCreating` no
  longer recurses infinitely or drops EF-derived composite key overrides.

### Removed

- Removed the vestigial match-info API surface (breaking, pre-1.0): `SearchMatchInfo`, the
  `match_info` query parameter, and all related request/response plumbing. The feature was
  already dead — every engine result site hardcoded `MatchInfo = null`.

### Added

- Actionable error when a required PostgreSQL extension (`pg_trgm` or `vector`) is missing,
  instead of a raw `PostgresException` escaping. Points at `docs/migrations.md`.
- Full-text search and multisearch coverage in the `Ferret.Samples.ExistingApi` sample.

### Documentation

- Added `SECURITY.md` (vulnerability reporting and release verification).
- Operator table, backend-choice guidance, `IFerretQueryService` xml-docs, and round-trip
  claim scoping.
