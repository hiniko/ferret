using System.Data.Common;
using Ferret.Abstractions;
using Ferret.Core.IntegrationTests.Fixtures;
using Ferret.Hydration.Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.FullText;

[Collection("postgres")]
public class FullTextEndToEndTests
{
    private readonly PostgresFixture _fx;

    public FullTextEndToEndTests(PostgresFixture fx) => _fx = fx;

    [SearchableEntity(Table = "docs")]
    [SearchGroup("content", FullTextConfig = "english")]
    public sealed class Doc : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 2.0f)]
        public string Title { get; init; } = "";

        [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 1.0f)]
        public string Body { get; init; } = "";
    }

    [Fact]
    public async Task Ranks_by_title_weight_over_body()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        await Exec(conn, """
            DROP TABLE IF EXISTS docs_search CASCADE;
            DROP TABLE IF EXISTS docs CASCADE;
            CREATE TABLE docs (id uuid PRIMARY KEY, title text NOT NULL, body text NOT NULL);
            CREATE TABLE docs_search (
                id uuid PRIMARY KEY REFERENCES docs(id) ON DELETE CASCADE,
                content_tsv tsvector,
                updated_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX ix_docs_search_content_tsv_gin ON docs_search USING gin (content_tsv);

            CREATE OR REPLACE FUNCTION docs_search_sync() RETURNS trigger AS $$
            BEGIN
                INSERT INTO docs_search (id, content_tsv, updated_at)
                VALUES (NEW.id,
                    setweight(to_tsvector('english', coalesce(NEW.title, '')), 'A') ||
                    setweight(to_tsvector('english', coalesce(NEW.body,  '')), 'B'),
                    now())
                ON CONFLICT (id) DO UPDATE
                SET content_tsv = EXCLUDED.content_tsv, updated_at = now();
                RETURN NEW;
            END $$ LANGUAGE plpgsql;

            CREATE TRIGGER docs_search_sync_t
                AFTER INSERT OR UPDATE OF title, body ON docs
                FOR EACH ROW EXECUTE FUNCTION docs_search_sync();
            """);

        var titleHit = Guid.NewGuid();
        var bodyHit  = Guid.NewGuid();
        await Exec(conn,
            $"INSERT INTO docs VALUES " +
            $"('{titleHit}', 'Concurrent reindexing patterns', 'irrelevant')," +
            $"('{bodyHit}',  'unrelated', 'a paragraph mentioning reindexing once');");

        var sp = BuildServices();
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();

        await using var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_fx.ConnectionString)),
            dialect);

        var result = await engine.SearchOffsetAsync<Doc, Guid>(session,
            new PagedQuery<Doc, Guid> { Mode = PaginationMode.Offset, Search = "reindexing", Limit = 10 });

        result.Items.Should().HaveCount(2);
        result.Items[0].Id.Should().Be(titleHit, "title is weighted A, body is weighted B");
        result.Items[1].Id.Should().Be(bodyHit);
    }

    [SearchableEntity(Table = "products")]
    [SearchGroup("content", FullTextConfig = "simple")]
    [SearchGroup("tags", FullTextConfig = "simple")]
    public sealed class Product : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 1.0f), Filterable]
        public string Name { get; init; } = "";

        [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 1.0f)]
        public string Description { get; init; } = "";

        [Searchable(Backend = SearchBackend.FullText, Group = "tags", Weight = 1.0f)]
        public string Tags { get; init; } = "";
    }

    [Fact]
    public async Task Single_search_hits_multiple_groups()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        await Exec(conn, """
            DROP TABLE IF EXISTS products_search CASCADE;
            DROP TABLE IF EXISTS products CASCADE;
            CREATE TABLE products (
                id uuid PRIMARY KEY,
                name text NOT NULL,
                description text NOT NULL,
                tags text NOT NULL
            );
            CREATE TABLE products_search (
                id uuid PRIMARY KEY REFERENCES products(id) ON DELETE CASCADE,
                content_tsv tsvector,
                tags_tsv tsvector,
                updated_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX ix_products_search_content_tsv_gin ON products_search USING gin (content_tsv);
            CREATE INDEX ix_products_search_tags_tsv_gin    ON products_search USING gin (tags_tsv);

            CREATE OR REPLACE FUNCTION products_search_sync() RETURNS trigger AS $$
            BEGIN
                INSERT INTO products_search (id, content_tsv, tags_tsv, updated_at)
                VALUES (NEW.id,
                    setweight(to_tsvector('simple', coalesce(NEW.name, '')),        'A') ||
                    setweight(to_tsvector('simple', coalesce(NEW.description, '')), 'B'),
                    setweight(to_tsvector('simple', coalesce(NEW.tags, '')),        'A'),
                    now())
                ON CONFLICT (id) DO UPDATE
                SET content_tsv = EXCLUDED.content_tsv,
                    tags_tsv    = EXCLUDED.tags_tsv,
                    updated_at  = now();
                RETURN NEW;
            END $$ LANGUAGE plpgsql;

            CREATE TRIGGER products_search_sync_t
                AFTER INSERT OR UPDATE OF name, description, tags ON products
                FOR EACH ROW EXECUTE FUNCTION products_search_sync();
            """);

        var contentHit = Guid.NewGuid();
        var tagsHit    = Guid.NewGuid();
        await Exec(conn,
            $"INSERT INTO products VALUES " +
            $"('{contentHit}', 'A widget', 'a widget for woodworking projects', 'kitchenware')," +
            $"('{tagsHit}',    'A gadget', 'unrelated body text',                 'woodworking,tools');");

        var sp = BuildServices();
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();

        await using var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_fx.ConnectionString)),
            dialect);

        var result = await engine.SearchOffsetAsync<Product, Guid>(session,
            new PagedQuery<Product, Guid> { Mode = PaginationMode.Offset, Search = "woodworking", Limit = 10 });

        result.Items.Should().HaveCount(2);
        result.Items.Select(p => p.Id).Should().BeEquivalentTo(new[] { contentHit, tagsHit });

        // Same search restricted by a Filter on the Name column should drop contentHit (Name = "A widget")
        // and leave only tagsHit (Name = "A gadget").
        var filtered = await engine.SearchOffsetAsync<Product, Guid>(session,
            new PagedQuery<Product, Guid>
            {
                Mode = PaginationMode.Offset,
                Search = "woodworking",
                Limit = 10,
                Filter = [new FilterClause { Field = nameof(Product.Name), Operator = FilterOperator.Equals, Value = "A gadget" }],
            });

        filtered.Items.Should().ContainSingle(p => p.Id == tagsHit);
    }

    private ServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts => opts
            .ScanAssembly(typeof(Doc).Assembly)
            .UsePostgres()
            .UseFullTextSearch(ft => ft.DefaultConfig = "english")
            .UseDapperHydration());
        return sc.BuildServiceProvider();
    }

    private static async Task Exec(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
