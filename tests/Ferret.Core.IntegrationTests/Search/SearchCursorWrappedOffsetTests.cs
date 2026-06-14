using System.Data.Common;
using Dapper;
using Ferret.Abstractions;
using Ferret.Core.IntegrationTests.Fixtures;
using Ferret.Hydration.Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.Search;

[Collection("postgres")]
public class SearchCursorWrappedOffsetTests
{
    private readonly PostgresFixture _fx;

    public SearchCursorWrappedOffsetTests(PostgresFixture fx) => _fx = fx;

    [SearchableEntity(Table = "rdocs")]
    [SearchGroup("content", FullTextConfig = "english")]
    public sealed class RDoc : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 1.0f)]
        public string Title { get; init; } = "";
    }

    [Fact]
    public async Task Cursor_pages_advance_disjointly_until_exhausted()
    {
        await SeedAsync(12);

        await using var sp = BuildServices();
        var (engine, session) = Open(sp);
        await using var _ = session;

        var page1 = await engine.SearchCursorAsync<RDoc, Guid>(session, new PagedQuery<RDoc, Guid>
        {
            Mode = PaginationMode.Cursor,
            Search = "report",
            Limit = 5,
        });

        page1.Items.Should().HaveCount(5);
        page1.NextCursor.Should().NotBeNull();
        page1.HasMore.Should().BeTrue();

        var page2 = await engine.SearchCursorAsync<RDoc, Guid>(session, new PagedQuery<RDoc, Guid>
        {
            Mode = PaginationMode.Cursor,
            Search = "report",
            Limit = 5,
            Cursor = page1.NextCursor,
            CursorDirection = CursorDirection.Forward,
        });

        page2.Items.Should().HaveCount(5);
        page2.Items.Select(d => d.Id).Should().NotIntersectWith(page1.Items.Select(d => d.Id));
        page2.NextCursor.Should().NotBeNull();
        page2.HasMore.Should().BeTrue();

        var page3 = await engine.SearchCursorAsync<RDoc, Guid>(session, new PagedQuery<RDoc, Guid>
        {
            Mode = PaginationMode.Cursor,
            Search = "report",
            Limit = 5,
            Cursor = page2.NextCursor,
            CursorDirection = CursorDirection.Forward,
        });

        page3.Items.Should().HaveCount(2);
        page3.Items.Select(d => d.Id).Should().NotIntersectWith(page1.Items.Select(d => d.Id));
        page3.Items.Select(d => d.Id).Should().NotIntersectWith(page2.Items.Select(d => d.Id));
        page3.NextCursor.Should().BeNull();
        page3.HasMore.Should().BeFalse();

        var allIds = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(d => d.Id).ToList();
        allIds.Should().OnlyHaveUniqueItems();
        allIds.Should().HaveCount(12);
    }

    [Fact]
    public async Task Cursor_stops_at_MaxSearchCursorOffset_cap_even_with_rows_remaining()
    {
        // 20 matching docs, cap=10, limit=5. Offsets walk 0 -> 5 -> 10.
        // At offset 10 the full page (rows 10..14) still returns, but nextOffset=15 > cap=10,
        // so NextCursor is suppressed and HasMore is false WHILE rows 15..19 still exist.
        // This isolates the cap from natural exhaustion (a partial last page).
        await SeedAsync(20);

        await using var sp = BuildServices(maxSearchCursorOffset: 10);
        var (engine, session) = Open(sp);
        await using var _ = session;

        string? cursor = null;
        var pages = 0;
        CursorResult<RDoc> page;
        do
        {
            page = await engine.SearchCursorAsync<RDoc, Guid>(session, new PagedQuery<RDoc, Guid>
            {
                Mode = PaginationMode.Cursor,
                Search = "report",
                Limit = 5,
                Cursor = cursor,
                CursorDirection = CursorDirection.Forward,
            });
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor != null && pages < 10);

        // offset 0 (page1), offset 5 (page2), offset 10 (page3) then cap halts.
        pages.Should().Be(3);
        page.Items.Should().HaveCount(5, "the page at the cap offset still returns a full page of rows");
        page.NextCursor.Should().BeNull("nextOffset 15 exceeds MaxSearchCursorOffset 10");
        page.HasMore.Should().BeFalse("the cap halts paging even though rows 15..19 still exist");
    }

    [Fact]
    public async Task Cursor_minted_for_one_search_term_rejected_for_another()
    {
        await SeedAsync(12);

        await using var sp = BuildServices();
        var (engine, session) = Open(sp);
        await using var _ = session;

        var page1 = await engine.SearchCursorAsync<RDoc, Guid>(session, new PagedQuery<RDoc, Guid>
        {
            Mode = PaginationMode.Cursor,
            Search = "report",
            Limit = 5,
        });

        page1.NextCursor.Should().NotBeNull();

        var act = async () => await engine.SearchCursorAsync<RDoc, Guid>(session, new PagedQuery<RDoc, Guid>
        {
            Mode = PaginationMode.Cursor,
            Search = "different",
            Limit = 5,
            Cursor = page1.NextCursor,
            CursorDirection = CursorDirection.Forward,
        });

        await act.Should().ThrowAsync<InvalidCursorException>();
    }

    private (IFerretEngine Engine, DapperSession Session) Open(ServiceProvider sp)
    {
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_fx.ConnectionString)),
            dialect);
        return (engine, session);
    }

    private async Task SeedAsync(int count)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync("""
            DROP TABLE IF EXISTS rdocs_search CASCADE;
            DROP TABLE IF EXISTS rdocs CASCADE;
            CREATE TABLE rdocs (id uuid PRIMARY KEY, title text NOT NULL);
            CREATE TABLE rdocs_search (
                id uuid PRIMARY KEY REFERENCES rdocs(id) ON DELETE CASCADE,
                content_tsv tsvector,
                updated_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX ix_rdocs_search_content_tsv_gin ON rdocs_search USING gin (content_tsv);

            CREATE OR REPLACE FUNCTION rdocs_search_sync() RETURNS trigger AS $$
            BEGIN
                INSERT INTO rdocs_search (id, content_tsv, updated_at)
                VALUES (NEW.id,
                    setweight(to_tsvector('english', coalesce(NEW.title, '')), 'A'),
                    now())
                ON CONFLICT (id) DO UPDATE
                SET content_tsv = EXCLUDED.content_tsv, updated_at = now();
                RETURN NEW;
            END $$ LANGUAGE plpgsql;

            CREATE TRIGGER rdocs_search_sync_t
                AFTER INSERT OR UPDATE OF title ON rdocs
                FOR EACH ROW EXECUTE FUNCTION rdocs_search_sync();
            """);

        // Every doc matches the FT term "report"; the numbered suffix keeps titles distinct.
        var rows = Enumerable.Range(0, count).Select(i => new
        {
            Id = Guid.NewGuid(),
            Title = $"quarterly report number {i:D3}",
        }).ToArray();

        await conn.ExecuteAsync("INSERT INTO rdocs (id, title) VALUES (@Id, @Title)", rows);
    }

    private ServiceProvider BuildServices(int? maxSearchCursorOffset = null)
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts =>
        {
            opts.ScanAssembly(typeof(RDoc).Assembly)
                .UsePostgres()
                .UseFullTextSearch(ft => ft.DefaultConfig = "english")
                .UseDapperHydration();
            if (maxSearchCursorOffset is { } cap)
                opts.UseHybridSearch(h => h.MaxSearchCursorOffset = cap);
        });
        return sc.BuildServiceProvider();
    }
}
