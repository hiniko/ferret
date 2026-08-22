# Filtering and vector search

Combining a filter with a vector search is harder than it looks, and the failure is quiet: you
ask for 25 results and get a handful, with no error. This page explains why, what the wider ecosystem
does about it, and where Ferret currently stands.

If you only want the practical rule, skip to [What to do today](#what-to-do-today).

## The shape of the problem

An HNSW index answers exactly one question: *what are the k nearest vectors to this one, across
the whole table?* It is a graph: the search walks from entry point to neighbour, keeping the
closest candidates it has seen, and stops when the frontier stops improving. `ef_search`
controls how many candidates it keeps in flight.

That graph knows nothing about your `WHERE` clause.

So when you write what seems like a pre-filter:

```sql
SELECT * FROM docs
WHERE tenant_id = 'acme'
ORDER BY embedding <=> :q
LIMIT 25;
```

Postgres cannot execute it in that order. The index produces its candidates first, and the
filter is applied to what comes out. If `acme` is 1% of the table, roughly 1% of those
candidates survive.

The order you wrote is not the order it runs.

### Measured

50,000 rows, 1% matching the filter, `LIMIT 25`, `ef_search = 40`, pgvector 0.8.2:

| Approach | Rows returned |
|---|---|
| Filter applied via `EXISTS` against the base table | **4–6** |
| Plus `hnsw.iterative_scan = relaxed_order` | 6 |
| Plus `iterative_scan` and `max_scan_tuples = 200000` | 6 |
| Exact scan, index disabled | **25** |
| Filter column colocated with the vector, plus `iterative_scan` | **25** |

Reproduce it:

```sql
CREATE EXTENSION vector;
CREATE TABLE docs (id int PRIMARY KEY, region text);
CREATE TABLE docs_vec (id int PRIMARY KEY, emb vector(8));
INSERT INTO docs
    SELECT g, CASE WHEN g % 100 = 0 THEN 'emea' ELSE 'apac' END
    FROM generate_series(1, 50000) g;
INSERT INTO docs_vec
    SELECT g, (SELECT array_agg(random())::vector(8) FROM generate_series(1, 8))
    FROM generate_series(1, 50000) g;
CREATE INDEX ON docs_vec USING hnsw (emb vector_cosine_ops);
ANALYZE;

SET LOCAL hnsw.ef_search = 40;
WITH candidates_ids AS (SELECT e.id FROM docs e WHERE e.region = 'emea')
SELECT count(*) FROM (
    SELECT s.id FROM docs_vec s
    WHERE s.emb IS NOT NULL
      AND EXISTS (SELECT 1 FROM candidates_ids c WHERE c.id = s.id)
    ORDER BY s.emb <=> (SELECT emb FROM docs_vec WHERE id = 1)
    LIMIT 25
) knn;   -- returns a handful, not 25
```

The exact count varies between runs — the vectors are random, so how many of the tenant's rows
happen to fall near the query vector is chance. What does not vary is that it is far short of
25.

`EXPLAIN` shows the mechanism — an `Index Scan` on the HNSW index feeding a `Nested Loop` that
discards most of what the index produced.

### Why `iterative_scan` does not rescue it here

pgvector 0.8 added iterative scans precisely for filtered search: keep pulling more candidates
until enough survive the filter, up to `max_scan_tuples`. It helps when the predicate is on the
**same table the index is on**, because the scan can evaluate it.

It cannot help when the predicate is evaluated by a join above the scan — the scan has no idea
which rows will survive, so "keep going until enough survive" has nothing to test against.
Raising `max_scan_tuples` changes nothing, as the table above shows.

## What everyone else does

This is a well-known problem with an established answer.

**pgvector** documents filtered search as a weak spot. [Issue #980](https://github.com/pgvector/pgvector/issues/980)
requests filtered HNSW and closed without resolution. Recommended workarounds are partial
indexes for a small number of filter values, B-tree plus exact scan for selective filters, or
iterative scans.

**The research literature** converges on selectivity-based routing.
[FAVOR](https://arxiv.org/pdf/2605.07770) proposes a "selectivity-driven search selector,
leveraging pre-filtering brute-force for low-selectivity queries and HNSW-based filtered ANN for
other selectivity levels". The [VLDB survey](https://www.vldb.org/pvldb/vol18/p5488-caminal.pdf)
and an [experimental study of filter-agnostic search on Postgres](https://arxiv.org/pdf/2603.23710)
reach the same conclusion: no single strategy dominates, so choose per query.

**Production systems** implement exactly that. Per
[Elasticsearch Labs](https://www.elastic.co/search-labs/blog/filtered-hnsw-knn-search) and the
[kNN docs](https://www.elastic.co/docs/solutions/search/vector/knn), Lucene:

- brute-forces the filtered set when the filtered document count is `<= num_candidates`;
- abandons the graph mid-traversal and switches to brute force when nodes explored exceeds the
  number of matching documents.

Elastic's practical guidance is that under roughly 10,000 vectors after filtering, brute force
should be the first choice. Qdrant makes the same call on "filter cardinality".

The consistent theme: **a selective filter should not use the ANN index at all.** Exact search
over a small filtered set is both correct and fast, and the index was never earning its keep on
that query.

## Why brute force is not a cop-out

An exact scan computes the distance to every vector in the filtered set. That sounds expensive
until you look at the numbers: a tenant slice of 10,000 vectors is a few milliseconds, and the
result is exactly right — no approximation, no truncation, no recall tuning.

The ANN index exists for the case where the candidate set is the whole table and exact scanning
would be far too slow. Once a filter has cut the table down, the premise no longer holds.

## Where Ferret stands today

Ferret routes filtered vector searches on estimated selectivity: a selective filter is executed
as an **exact scan** over the filtered set — correct by construction, no truncation — and a mild
one still uses the HNSW index, with pgvector's iterative scan enabled to recover recall.

Configure it on `VectorOptions`:

```csharp
services.AddFerret(o => o.UseVectorSearch(v =>
{
    v.FilterStrategy     = VectorFilterStrategy.Auto;  // Auto | AlwaysExact | AlwaysIndex
    v.ExactScanThreshold = 10_000;   // route to exact scan at or below this estimate
    v.MaxExactScanRows   = 250_000;  // safety valve if the estimate was badly wrong
    v.IterativeScan      = HnswIterativeScan.RelaxedOrder;
    v.MaxScanTuples      = 20_000;
}));
```

`AlwaysExact` is worth considering if your vector searches are always tenant-scoped: it skips
estimation entirely, so results never depend on how fresh your table statistics are.

Two things were already correct and are worth stating:

- Filters are **pushed into the database**. Ferret compiles `Filter` into the search query rather
  than materialising matching keys in the application.
- Trigram and full-text search are **unaffected**. GiST and GIN are exact indexes: filtering
  changes which rows match, never how many the index can find.

Known gap: **filter + hybrid search** throws `NotSupportedException` (a v1 limitation predating
this work), so the routing does not apply there. Use a per-query `Backend` override to search a
single backend with filters.

## Tuning

**If your vector searches are always tenant-scoped or otherwise selective**, set
`FilterStrategy = AlwaysExact`. You skip the per-query estimate, results are exact, and nothing
depends on statistics freshness.

**If selectivity varies**, leave `Auto` and tune `ExactScanThreshold` against your own data. The
default of 10,000 is a starting point, not a universal answer — the right value depends on vector
dimensionality, row width and your latency budget. For reference, on 384-dimension vectors an
exact scan over 40,000 rows measured ~30 ms, and over 200,000 rows ~38 ms; it scales far better
than linearly, because the work is a bounded top-k heap over a sequential scan.

**If you have already tuned around the old behaviour**, `AlwaysIndex` preserves it exactly.

**Keep statistics fresh.** Under `Auto`, routing uses the planner's estimate, and a bulk insert
without an `ANALYZE` can leave it wildly wrong — measured 144× low. The result stays correct
either way (an exact scan cannot truncate, and the `MaxExactScanRows` valve bounds a runaway
scan), but a badly stale estimate can route to a slower path than necessary.

## See also

- [vector-search.md](vector-search.md) — configuring the vector backend, connectors, reindexing
- [full-text-search.md](full-text-search.md) — full-text and hybrid, neither of which is affected
