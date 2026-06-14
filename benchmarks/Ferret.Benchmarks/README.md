# Ferret.Benchmarks

BenchmarkDotNet harness for Ferret. The benchmarks spin up a real PostgreSQL
instance via Testcontainers, so **Docker must be running** before you start a run.

## Prerequisites

- Docker (Testcontainers pulls `postgres:17-alpine` by default).
- A Release build — BenchmarkDotNet refuses to run against Debug assemblies.

## Running the suite

```bash
# requires Docker for Testcontainers
dotnet run -c Release --project benchmarks/Ferret.Benchmarks
```

To pin or override the PostgreSQL image (for example in CI, or to match a
production server version), set `FERRET_POSTGRES_IMAGE`:

```bash
FERRET_POSTGRES_IMAGE=postgres:16 dotnet run -c Release --project benchmarks/Ferret.Benchmarks
```

The same `FERRET_POSTGRES_IMAGE` override is honoured by the integration test
harnesses.

## SearchJoin depth benchmark

`SearchJoinDepthBenchmark` measures the cost of multi-hop `[SearchJoin]` queries
up to the 5-hop cap. See
[`docs/superpowers/specs/2026-05-31-searchjoin-bench-findings.md`](../../docs/superpowers/specs/2026-05-31-searchjoin-bench-findings.md)
for results and the `HopBudget` recommendation.

## CI note

The integration tests are written as `SkippableFact`s — without Docker available
they are **skipped** rather than failed, so `dotnet test` stays green on machines
without a container runtime. The benchmarks themselves always require Docker and
a Release build.
