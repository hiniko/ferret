using System.Data.Common;
using System.Diagnostics;
using Dapper;
using Ferret.Abstractions;
using Ferret.Core.Diagnostics;
using Ferret.Core.IntegrationTests.Fixtures;
using Ferret.Hydration.Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests;

[Collection("postgres")]
public class DiagnosticsEndToEndTests
{
    private readonly PostgresFixture _fx;

    public DiagnosticsEndToEndTests(PostgresFixture fx) => _fx = fx;

    [SearchableEntity(Table = "widgets")]
    public sealed class Widget : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Filterable, Sortable] public string Name { get; init; } = "";
        [Searchable] public string Sku { get; init; } = "";
    }

    [Fact]
    public async Task Offset_query_emits_parent_and_db_activities_with_tags()
    {
        await Seed();
        var activities = new List<Activity>();
        using var listener = StartListener(activities);

        var engine = BuildEngine();
        var result = await engine.SearchOffsetAsync<Widget, Guid>(NewSession(), new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Offset,
            Limit = 25,
            Page = 0,
            Filter = [new FilterClause { Field = "Name", Operator = FilterOperator.Equals, Value = "Blue Widget" }],
        });

        result.Items.Should().HaveCount(1);

        var parent = activities.Should().ContainSingle(a => a.OperationName == "ferret.search.offset").Subject;
        parent.GetTagItem("ferret.entity").Should().Be("widgets");
        parent.GetTagItem("ferret.mode").Should().Be("offset");
        parent.GetTagItem("ferret.filter.count").Should().Be(1);
        parent.GetTagItem("ferret.row.count").Should().Be(1);
        parent.GetTagItem("ferret.total_count").Should().Be(1);
        parent.GetTagItem("ferret.duration_ms").Should().BeOfType<double>();

        activities.Should().Contain(a => a.OperationName == "ferret.db.query.ids" && a.Parent == parent);
        activities.Should().Contain(a => a.OperationName == "ferret.hydrate" && a.Parent == parent);

        var dbSpan = activities.First(a => a.OperationName == "ferret.db.query.ids");
        dbSpan.GetTagItem("db.system").Should().Be("postgresql");
        dbSpan.GetTagItem("ferret.row.count").Should().Be(1);
    }

    [Fact]
    public async Task Search_query_emits_candidates_span_with_backend_tag()
    {
        await Seed();
        var activities = new List<Activity>();
        using var listener = StartListener(activities);

        var engine = BuildEngine();
        await engine.SearchOffsetAsync<Widget, Guid>(NewSession(), new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "BLUE",
            Limit = 25,
            Page = 0,
        });

        var candidates = activities.Should()
            .ContainSingle(a => a.OperationName == "ferret.search.candidates").Subject;
        candidates.GetTagItem("ferret.backend").Should().Be("trigram");
        candidates.GetTagItem("ferret.entity").Should().Be("widgets");
    }

    [Fact]
    public async Task Engine_failure_records_error_status_and_logs_error()
    {
        await Seed();
        var activities = new List<Activity>();
        using var listener = StartListener(activities);

        var provider = new RecordingLoggerProvider();
        var engine = BuildEngine(provider);
        var sink = provider.Logger;

        var act = async () => await engine.SearchOffsetAsync<Widget, Guid>(NewSession(), new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Offset,
            Limit = 25,
            Page = 0,
            Filter = [new FilterClause { Field = "NonExistent", Operator = FilterOperator.Equals, Value = "x" }],
        });
        await act.Should().ThrowAsync<InvalidOperationException>();

        var parent = activities.Single(a => a.OperationName == "ferret.search.offset");
        parent.Status.Should().Be(ActivityStatusCode.Error);
        sink.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Statement_logging_disabled_by_default_omits_db_statement_tag()
    {
        await Seed();
        var activities = new List<Activity>();
        using var listener = StartListener(activities);

        var engine = BuildEngine();
        await engine.SearchOffsetAsync<Widget, Guid>(NewSession(), new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Offset,
            Limit = 25,
            Page = 0,
        });

        var dbSpan = activities.First(a => a.OperationName == "ferret.db.query.ids");
        dbSpan.GetTagItem("db.statement").Should().BeNull();
    }

    [Fact]
    public async Task Statement_logging_enabled_attaches_db_statement_tag()
    {
        await Seed();
        var activities = new List<Activity>();
        using var listener = StartListener(activities);

        var engine = BuildEngine(opts: o => o.WithStatementLogging());
        await engine.SearchOffsetAsync<Widget, Guid>(NewSession(), new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Offset,
            Limit = 25,
            Page = 0,
        });

        var dbSpan = activities.First(a => a.OperationName == "ferret.db.query.ids");
        dbSpan.GetTagItem("db.statement").Should().NotBeNull();
    }

    private static ActivityListener StartListener(List<Activity> sink)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == FerretDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => sink.Add(a),
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private DapperSession NewSession()
    {
        var dialect = new Core.Sql.PostgresDialect();
        return new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_fx.ConnectionString)),
            dialect);
    }

    private IFerretEngine BuildEngine(ILoggerProvider? provider = null, Action<Core.Configuration.FerretOptions>? opts = null)
    {
        var sc = new ServiceCollection();
        sc.AddLogging(lb => { if (provider is not null) lb.AddProvider(provider); });
        sc.AddFerret(o =>
        {
            o.ScanAssembly(typeof(Widget).Assembly)
                .UseTrigramSearch()
                .UseDapperHydration();
            opts?.Invoke(o);
        });
        return sc.BuildServiceProvider().GetRequiredService<IFerretEngine>();
    }

    private async Task Seed()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("TRUNCATE widgets");
        await conn.ExecuteAsync(
            "INSERT INTO widgets (id, name, sku) VALUES (@Id, @Name, @Sku)",
            new[]
            {
                new { Id = Guid.NewGuid(), Name = "Blue Widget",  Sku = "BLUE-001" },
                new { Id = Guid.NewGuid(), Name = "Red Widget",   Sku = "RED-001" },
            });
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
