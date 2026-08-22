using System.Data.Common;
using System.Globalization;
using Dapper;
using Ferret.Abstractions;
using Ferret.Core.Backends.Hybrid;
using Ferret.Core.Backends.Vector;
using Ferret.Core.Embeddings;
using Ferret.Core.Engine;
using Ferret.Core.IntegrationTests.Fixtures;
using Ferret.Hydration.Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.Vector;

/// <summary>
/// Edge-path coverage for selectivity-based filter routing (docs/vector-filtering.md):
/// the exact-scan statement-timeout fallback, the hybrid+filter v1 guard, the
/// <see cref="VectorFilterStrategy.Auto"/> boundary at exactly <c>ExactScanThreshold</c>, an
/// always-false filter, and both <see cref="HnswIterativeScan"/> modes with a filter. The happy
/// paths (50k-row selective/mild filters) are covered by
/// <see cref="VectorFilterRoutingEndToEndTests"/> — this file only adds what that one doesn't
/// exercise. Same collection: <see cref="FerretEngine.ExactScanStatementTimeoutMs"/> is mutated
/// process-global static state, and xunit collections already serialize test execution within a
/// collection, so it must not run concurrently with the other routing tests.
/// </summary>
[Collection("pgvector")]
public class VectorFilterRoutingEdgeTests
{
    private readonly PgVectorFixture _fx;
    private const int Dimensions = 8;
    private const int RowCount = 2_000;
    // Sized so a real sequential scan (the exact-scan path's mechanism) reliably takes longer
    // than the 1ms statement_timeout the timeout-fallback test sets — a 2k-row scan can finish
    // inside 1ms often enough to make that assertion flaky.
    private const int TimeoutTestRowCount = 200_000;
    // "emea" is assigned to 1 row in 100 (~1% selectivity), same shape as the e2e fixture.
    private const int SelectiveModulus = 100;

    public VectorFilterRoutingEdgeTests(PgVectorFixture fx) => _fx = fx;

    [SearchableEntity(Table = "vdocs_routing_edge")]
    public sealed class RoutedDoc : ISearchableEntity<int>
    {
        public int Id { get; init; }

        [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = Dimensions)]
        public string Body { get; init; } = "";

        [Filterable]
        public string Region { get; init; } = "";
    }

    [SearchableEntity(Table = "vdocs_routing_edge")]
    [SearchGroup("content", FullTextConfig = "english")]
    public sealed class HybridDoc : ISearchableEntity<int>
    {
        public int Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "content")]
        public string Body { get; init; } = "";

        [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = Dimensions)]
        public string Vec { get; init; } = "";

        [Filterable]
        public string Region { get; init; } = "";
    }

    private const string SearchTerm = "seed document for nearest neighbour search";

    [SkippableFact]
    public async Task Exact_scan_timeout_falls_back_to_index_path_without_throwing()
    {
        // FerretEngine.ExactScanStatementTimeoutMs is process-global static state — restore it
        // even on failure so other tests (and the pgvector collection) aren't poisoned.
        var original = FerretEngine.ExactScanStatementTimeoutMs;
        FerretEngine.ExactScanStatementTimeoutMs = 1;
        try
        {
            var provider = new FakeEmbeddingProvider(Dimensions);
            await using var conn = new NpgsqlConnection(_fx.ConnectionString);
            await conn.OpenAsync();
            // A 1ms budget must reliably lose the race against a real exact scan: the small
            // (2k-row) table used by the other edge tests is fast enough that the scan
            // sometimes finishes inside 1ms, making the fallback assertion flaky. Seed a
            // larger table here so the sequential scan the exact-scan path forces is
            // guaranteed to exceed the budget.
            await SeedRoutingTableAsync(conn, provider, rowCount: TimeoutTestRowCount);

            var loggerProvider = new RecordingLoggerProvider();
            await using var sp = BuildServices<RoutedDoc>(provider, loggerProvider, v =>
            {
                v.EfSearch = 40;
                // Force the exact-scan route regardless of the estimate, so the 1ms budget is
                // certain to be exceeded by a real query and trip the fallback.
                v.FilterStrategy = VectorFilterStrategy.AlwaysExact;
            });
            var engine = sp.GetRequiredService<IFerretEngine>();
            var dialect = sp.GetRequiredService<ISqlDialect>();
            await using var session = OpenSessionFor(dialect);

            const int limit = 10;
            var act = async () => await engine.SearchOffsetAsync<RoutedDoc, int>(session,
                new PagedQuery<RoutedDoc, int>
                {
                    Mode = PaginationMode.Offset,
                    Search = SearchTerm,
                    Limit = limit,
                    Filter = [new FilterClause { Field = "Region", Operator = FilterOperator.Equals, Value = "emea" }],
                });

            var result = await act.Should().NotThrowAsync(
                "the estimate-wrong safety valve must fall back to the index path, not surface the timeout");
            result.Subject.Items.Should().NotBeEmpty("the index-path fallback must still produce results");
            result.Subject.Items.Should().OnlyContain(d => d.Region == "emea");

            loggerProvider.Logger.Entries.Should().Contain(
                e => e.Level == LogLevel.Warning && e.Message.Contains("exceeded its time budget"),
                "the fallback must warn so an operator can see the estimate was wrong");
        }
        finally
        {
            FerretEngine.ExactScanStatementTimeoutMs = original;
        }
    }

    [SkippableFact]
    public async Task Hybrid_search_with_filter_throws_not_supported()
    {
        var provider = new FakeEmbeddingProvider(Dimensions);
        await using var sp = BuildHybridServices(provider);
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        await using var session = OpenSessionFor(dialect);

        var act = async () => await engine.SearchOffsetAsync<HybridDoc, int>(session,
            new PagedQuery<HybridDoc, int>
            {
                Mode = PaginationMode.Offset,
                Search = SearchTerm,
                Limit = 10,
                Filter = [new FilterClause { Field = "Region", Operator = FilterOperator.Equals, Value = "emea" }],
            });

        var ex = await act.Should().ThrowAsync<NotSupportedException>();
        ex.Which.Message.Should().Contain("not supported in v1");
    }

    [SkippableFact]
    public async Task Auto_strategy_routes_exact_scan_when_estimate_equals_threshold()
    {
        // ExecuteVectorFilteredRankingAsync routes `estimate <= ExactScanThreshold` to the exact
        // scan (FerretEngine.cs), and the XML doc on ExactScanThreshold says "at most this many
        // rows" — both already describe an inclusive (<=) boundary. This proves it: set the
        // threshold to exactly the real (ANALYZEd) filtered row count and confirm the exact-scan
        // route is taken by disabling the index globally being irrelevant — instead prove via
        // AlwaysIndex-vs-Auto behavioural parity: at the boundary Auto must return the full page
        // (only the exact-scan path guarantees that), matching AlwaysExact's behaviour, not
        // AlwaysIndex's (which the sibling e2e test proves under-returns at this selectivity).
        var provider = new FakeEmbeddingProvider(Dimensions);
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await SeedRoutingTableAsync(conn, provider);

        var emeaCount = await conn.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM vdocs_routing_edge WHERE region = 'emea'");

        await using var sp = BuildServices<RoutedDoc>(provider, loggerProvider: null, v =>
        {
            v.EfSearch = 40;
            v.FilterStrategy = VectorFilterStrategy.Auto;
            v.ExactScanThreshold = (int)emeaCount; // boundary: estimate should land at/near this
        });
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        await using var session = OpenSessionFor(dialect);

        const int limit = 10;
        var result = await engine.SearchOffsetAsync<RoutedDoc, int>(session,
            new PagedQuery<RoutedDoc, int>
            {
                Mode = PaginationMode.Offset,
                Search = SearchTerm,
                Limit = limit,
                Filter = [new FilterClause { Field = "Region", Operator = FilterOperator.Equals, Value = "emea" }],
            });

        result.Items.Should().HaveCount(limit,
            "an estimate at or below ExactScanThreshold (the documented inclusive boundary) must take " +
            "the exact-scan path and return the full requested page");
        result.Items.Should().OnlyContain(d => d.Region == "emea");
    }

    [SkippableFact]
    public async Task Always_false_filter_returns_empty_result_with_zero_total_and_no_exception()
    {
        var provider = new FakeEmbeddingProvider(Dimensions);
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await SeedRoutingTableAsync(conn, provider);

        await using var sp = BuildServices<RoutedDoc>(provider, loggerProvider: null, v =>
        {
            v.EfSearch = 40;
            v.FilterStrategy = VectorFilterStrategy.Auto;
        });
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        await using var session = OpenSessionFor(dialect);

        var act = async () => await engine.SearchOffsetAsync<RoutedDoc, int>(session,
            new PagedQuery<RoutedDoc, int>
            {
                Mode = PaginationMode.Offset,
                Search = SearchTerm,
                Limit = 10,
                Filter = [new FilterClause { Field = "Region", Operator = FilterOperator.Equals, Value = "does-not-exist" }],
            });

        var result = await act.Should().NotThrowAsync();
        result.Subject.Items.Should().BeEmpty();
        result.Subject.TotalCount.Should().Be(0);
    }

    [SkippableFact]
    public Task Index_path_succeeds_with_iterative_scan_StrictOrder()
        => AssertIndexPathSucceedsWithIterativeScanAsync(HnswIterativeScan.StrictOrder);

    [SkippableFact]
    public Task Index_path_succeeds_with_iterative_scan_Off()
        => AssertIndexPathSucceedsWithIterativeScanAsync(HnswIterativeScan.Off);

    private async Task AssertIndexPathSucceedsWithIterativeScanAsync(HnswIterativeScan mode)
    {
        var provider = new FakeEmbeddingProvider(Dimensions);
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await SeedRoutingTableAsync(conn, provider);

        await using var sp = BuildServices<RoutedDoc>(provider, loggerProvider: null, v =>
        {
            v.EfSearch = 40;
            // Force the index path so the configured IterativeScan mode is actually exercised
            // (the exact-scan route never touches iterative_scan at all).
            v.FilterStrategy = VectorFilterStrategy.AlwaysIndex;
            v.IterativeScan = mode;
        });
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        await using var session = OpenSessionFor(dialect);

        var result = await engine.SearchOffsetAsync<RoutedDoc, int>(session,
            new PagedQuery<RoutedDoc, int>
            {
                Mode = PaginationMode.Offset,
                Search = SearchTerm,
                Limit = 10,
                Filter = [new FilterClause { Field = "Region", Operator = FilterOperator.Equals, Value = "emea" }],
            });

        result.Items.Should().OnlyContain(d => d.Region == "emea",
            "the filter is applied post-index regardless of iterative_scan mode; every returned row must still match it");
    }

    private IFerretSession OpenSessionFor(ISqlDialect dialect)
    {
        var csb = new NpgsqlConnectionStringBuilder(_fx.ConnectionString) { PersistSecurityInfo = true };
        return new DapperSession(ct => Task.FromResult<DbConnection>(new NpgsqlConnection(csb.ConnectionString)), dialect);
    }

    private static ServiceProvider BuildServices<T>(
        FakeEmbeddingProvider provider, RecordingLoggerProvider? loggerProvider, Action<VectorOptions> configure)
        where T : class
    {
        var sc = new ServiceCollection();
        sc.AddLogging(lb => { if (loggerProvider is not null) lb.AddProvider(loggerProvider); });
        sc.AddFerret(o => o.ScanAssembly(typeof(T).Assembly)
            .UseVectorSearch(v =>
            {
                v.UseEmbeddingProvider(_ => provider);
                configure(v);
            }));
        return sc.BuildServiceProvider();
    }

    private static ServiceProvider BuildHybridServices(FakeEmbeddingProvider provider)
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(o => o.ScanAssembly(typeof(HybridDoc).Assembly)
            .UseFullTextSearch(ft => ft.DefaultConfig = "english")
            .UseVectorSearch(v =>
            {
                v.UseEmbeddingProvider(_ => provider);
                v.EfSearch = 40;
            })
            .UseHybridSearch(_ => { }));
        return sc.BuildServiceProvider();
    }

    /// <summary>
    /// Seeds a small (2k-row) routing table — enough to exercise real Postgres SQL/EXPLAIN
    /// behaviour without the 50k-row e2e fixture's runtime cost, which these edge tests don't
    /// need (they assert routing/fallback mechanics, not recall at scale).
    /// </summary>
    private static Task SeedRoutingTableAsync(NpgsqlConnection conn, FakeEmbeddingProvider provider)
        => SeedRoutingTableAsync(conn, provider, RowCount);

    private static async Task SeedRoutingTableAsync(NpgsqlConnection conn, FakeEmbeddingProvider provider, int rowCount)
    {
        var sidecar = VectorSidecarNaming.TableName("vdocs_routing_edge", new VectorOptions());
        var col     = VectorSidecarNaming.ColumnName("content", new VectorOptions(), VectorSidecarNaming.CurrentVersion);
        var idx     = VectorSidecarNaming.IndexName(sidecar, col);

        await conn.ExecuteAsync($"""
            DROP TABLE IF EXISTS "{sidecar}" CASCADE;
            DROP TABLE IF EXISTS vdocs_routing_edge CASCADE;
            CREATE TABLE vdocs_routing_edge (id int PRIMARY KEY, body text NOT NULL, region text NOT NULL);
            {VectorDdlBuilder.CreateVersionRegistry(null)}
            {VectorDdlBuilder.CreateSidecarTable(sidecar, null, "vdocs_routing_edge", null, "id", "int")}
            {VectorDdlBuilder.AddGroupColumn(sidecar, null, col, provider.Dimensions)}
            """);

        await conn.ExecuteAsync(
            "DELETE FROM ferret_vector_versions WHERE entity = @entity AND group_name = @group AND status = 'active'; " +
            "INSERT INTO ferret_vector_versions (entity, group_name, model, dimensions, column_name, status) " +
            "VALUES (@entity, @group, @model, @dims, @col, 'active')",
            new { entity = "vdocs_routing_edge", group = "content", model = "fake", dims = provider.Dimensions, col });

        await conn.ExecuteAsync($"""
            INSERT INTO vdocs_routing_edge (id, body, region)
            SELECT g, 'document number ' || g, CASE WHEN g % {SelectiveModulus} = 0 THEN 'emea' ELSE 'apac' END
            FROM generate_series(1, {rowCount}) g;

            INSERT INTO "{sidecar}" (id, "{col}")
            SELECT g, (SELECT array_agg(random())::vector({Dimensions}) FROM generate_series(1, {Dimensions}))
            FROM generate_series(1, {rowCount}) g;
            """, commandTimeout: 60);

        // One "emea" row's body/embedding is overwritten to exactly match the search term, so
        // the nearest-neighbour answer is well-defined and lands inside the filtered set.
        const int targetId = SelectiveModulus; // g % 100 == 0 -> "emea"
        var vec = await provider.EmbedAsync(SearchTerm, default);
        var lit = "[" + string.Join(",", vec.Select(f => f.ToString(CultureInfo.InvariantCulture))) + "]";
        await conn.ExecuteAsync(
            $"UPDATE vdocs_routing_edge SET body = @body WHERE id = @id; " +
            $"UPDATE \"{sidecar}\" SET \"{col}\" = @vec::vector WHERE id = @id;",
            new { id = targetId, body = SearchTerm, vec = lit });

        await conn.ExecuteAsync($"""
            {VectorDdlBuilder.CreateGroupIndex(sidecar, null, idx, col, m: 16, efConstruction: 64)}
            ANALYZE vdocs_routing_edge;
            ANALYZE "{sidecar}";
            """, commandTimeout: 60);
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public RecordingLogger Logger { get; } = new();
        public ILogger CreateLogger(string categoryName) => Logger;
        public void Dispose() { }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
        private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
    }
}
