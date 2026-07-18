# Changelog

Curated, human-written notes for each release — what changed and why it matters to
consumers, not a commit dump. GitHub releases carry the auto-generated commit log
between tags; this file is the readable story. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow SemVer
(tags are unprefixed, e.g. `0.1.2`).

## [0.1.2] - 2026-07-18

### Fixed

- **Ferret.AspNetCore**: pinned `Microsoft.OpenApi` to 2.11.0 as a direct reference,
  clearing the high-severity NU1903 advisory (GHSA-v5pm-xwqc-g5wc) that the
  transitive 2.0.0 from `Microsoft.AspNetCore.OpenApi` pulled in. A fresh restore of
  0.1.1 failed in repos that treat NuGet audit warnings as errors; 0.1.2 restores
  clean.

## [0.1.1] - 2026-06-26

### Fixed

- **Ferret.EntityFrameworkCore**: the hydrator now rewrites `SELECT *` into an
  explicit column list built from EF Core metadata. Entities mapped with `xmin` /
  `IsRowVersion()` concurrency tokens previously failed to load; they now hydrate
  correctly (covered by `XminHydrationTests`).
- **Ferret.EntityFrameworkCore**: owned-type and view-mapped columns are included in
  hydration, and projection handling was hardened against unmapped columns.
- **Ferret.EntityFrameworkCore**: the EF adapter now *replaces* the core
  `IFerretQueryService` registration instead of adding alongside it, so
  `ValidateOnBuild` / DI validation passes.

## [0.1.0] - 2026-06-25

### Added

- Initial release: `Ferret.Abstractions`, `Ferret.Core`,
  `Ferret.EntityFrameworkCore`, `Ferret.Hydration.Dapper`, `Ferret.AspNetCore`,
  `Ferret.Hosting`, `Ferret.Migrations`, `Ferret.Compat.LegacyApi`, and the
  `Ferret.Tools.Cli` tool — filtered querying, cursor pagination, full-text and
  vector search over PostgreSQL, with EF Core and Dapper hydration paths.
