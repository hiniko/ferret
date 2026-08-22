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
public class MultiSearchEndToEndTests
{
    private readonly PostgresFixture _fx;

    public MultiSearchEndToEndTests(PostgresFixture fx) => _fx = fx;

    [SearchableEntity(Table = "ms_authors")]
    public sealed class Author : ISearchableEntity<Guid>
    {
        public Guid Id { get; set; }
        [Searchable] public string Name { get; set; } = "";
        [Filterable] public string Email { get; set; } = "";
        public bool Hidden { get; set; }
    }

    [SearchableEntity(Table = "ms_books")]
    public sealed class Book : ISearchableEntity<Guid>
    {
        public Guid Id { get; set; }
        [Searchable] public string Title { get; set; } = "";
        public string Isbn { get; set; } = "";
    }

    [SearchableEntity(Table = "ms_editions", KeyProperties = [nameof(TenantId), nameof(Code)])]
    public sealed class Edition
    {
        public Guid TenantId { get; set; }
        public string Code { get; set; } = "";
        [Searchable] public string Label { get; set; } = "";
    }

    [SearchableEntity(Table = "ms_vdocs")]
    public sealed class VectorDoc : ISearchableEntity<Guid>
    {
        public Guid Id { get; set; }
        [Searchable(Backend = SearchBackend.Vector, EmbeddingDimensions = 8)] public string Content { get; set; } = "";
    }

    private static readonly Guid AuthorMoby   = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly Guid AuthorOther  = Guid.Parse("00000003-0000-0000-0000-000000000002");
    private static readonly Guid AuthorHidden = Guid.Parse("00000003-0000-0000-0000-000000000003");
    private static readonly Guid BookMoby     = Guid.Parse("00000003-0000-0000-0000-000000000011");
    private static readonly Guid Tenant       = Guid.Parse("00000003-0000-0000-0000-000000000021");

    [Fact]
    public async Task Grouped_hits_are_partial_entities_with_projected_members_only()
    {
        await SeedAsync();
        var (engine, session) = Engine();

        var result = await engine.MultiSearchAsync(session, q => q
            .Search("moby")
            .PerEntityLimit(5)
            .Entity<Author>(a => new Author { Name = a.Name, Email = a.Email })
            .Entity<Book>(b => new Book { Id = b.Id, Title = b.Title }));

        result.Groups.Should().HaveCount(2);
        result.Groups[0].EntityType.Should().Be("ms_authors");
        result.Groups[1].EntityType.Should().Be("ms_books");

        var author = result.Groups[0].Hits.Should().NotBeEmpty()
            .And.AllBeOfType<Author>().Subject.First();
        author.Name.Should().Be("Moby Dickens");
        author.Email.Should().Be("moby@sea.org");
        author.Id.Should().Be(Guid.Empty, "Id was not projected");

        var book = result.HitsOf<Book>().First();
        book.Title.Should().Be("Moby-Dick");
        book.Id.Should().Be(BookMoby, "Id was projected");
        book.Isbn.Should().BeEmpty("Isbn was not projected");
    }

    [Fact]
    public async Task Best_match_first_within_group()
    {
        await SeedAsync();
        var (engine, session) = Engine();

        var result = await engine.MultiSearchAsync(session, q => q
            .Search("moby dickens")
            .Entity<Author>(a => new Author { Name = a.Name }));

        var names = result.HitsOf<Author>().Select(h => h.Name).ToList();
        names.First().Should().Be("Moby Dickens");
    }

    [Fact]
    public async Task Per_entity_filter_excludes_matching_row()
    {
        await SeedAsync();
        var (engine, session) = Engine();

        var result = await engine.MultiSearchAsync(session, q => q
            .Search("moby")
            .Entity<Author>(a => new Author { Name = a.Name },
                o => o.Filter(new FilterClause
                {
                    Field = "Email", Operator = FilterOperator.NotEquals, Value = "moby@sea.org",
                })));

        result.HitsOf<Author>().Should().NotContain(h => h.Name == "Moby Dickens");
    }

    [Fact]
    public async Task No_matches_yields_empty_groups()
    {
        await SeedAsync();
        var (engine, session) = Engine();

        var result = await engine.MultiSearchAsync(session, q => q
            .Search("zzzzqqqq")
            .Entity<Author>(a => new Author { Name = a.Name })
            .Entity<Book>(b => new Book { Title = b.Title }));

        result.Groups.Should().HaveCount(2);
        result.Groups.Should().OnlyContain(g => g.Hits.Count == 0);
    }

    [Fact]
    public async Task Per_entity_limit_caps_each_group_independently()
    {
        await SeedAsync();
        var (engine, session) = Engine();

        var result = await engine.MultiSearchAsync(session, q => q
            .Search("moby")
            .PerEntityLimit(1)
            .Entity<Author>(a => new Author { Name = a.Name })
            .Entity<Book>(b => new Book { Title = b.Title }));

        result.Groups.Should().OnlyContain(g => g.Hits.Count <= 1);
        result.Groups[0].Hits.Should().HaveCount(1);
    }

    [Fact]
    public async Task Composite_key_entity_projects_key_parts()
    {
        await SeedAsync();
        var (engine, session) = Engine();

        var result = await engine.MultiSearchAsync(session, q => q
            .Search("collector")
            .Entity<Edition>(e => new Edition { TenantId = e.TenantId, Code = e.Code, Label = e.Label }));

        var hit = result.HitsOf<Edition>().Should().ContainSingle().Subject;
        hit.TenantId.Should().Be(Tenant);
        hit.Code.Should().Be("COLL-1");
        hit.Label.Should().Be("Collector Edition");
    }

    [Fact]
    public async Task Types_subsets_groups_and_unknown_type_throws()
    {
        await SeedAsync();
        var (engine, session) = Engine();

        var subset = await engine.MultiSearchAsync(session, q => q
            .Search("moby")
            .Types("ms_books")
            .Entity<Author>(a => new Author { Name = a.Name })
            .Entity<Book>(b => new Book { Title = b.Title }));

        subset.Groups.Should().ContainSingle().Which.EntityType.Should().Be("ms_books");

        await FluentActions.Awaiting(() => engine.MultiSearchAsync(session, q => q
                .Search("moby")
                .Types("nope")
                .Entity<Author>(a => new Author { Name = a.Name })))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*nope*ms_authors*");
    }

    [Fact]
    public async Task Vector_entity_is_rejected()
    {
        var (engine, session) = Engine();

        await FluentActions.Awaiting(() => engine.MultiSearchAsync(session, q => q
                .Search("moby")
                .Entity<VectorDoc>(v => new VectorDoc { Content = v.Content })))
            .Should().ThrowAsync<NotSupportedException>().WithMessage("*ms_vdocs*");
    }

    private (IFerretEngine Engine, DapperSession Session) Engine()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts => opts
            .ScanAssembly(typeof(Author).Assembly)
            .UseTrigramSearch());
        var sp = sc.BuildServiceProvider();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_fx.ConnectionString)),
            dialect);
        return (sp.GetRequiredService<IFerretEngine>(), session);
    }

    private async Task SeedAsync()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS ms_authors (
                id uuid PRIMARY KEY,
                name text NOT NULL,
                email text NOT NULL,
                hidden boolean NOT NULL DEFAULT false
            );
            CREATE TABLE IF NOT EXISTS ms_books (
                id uuid PRIMARY KEY,
                title text NOT NULL,
                isbn text NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ms_editions (
                tenant_id uuid NOT NULL,
                code text NOT NULL,
                label text NOT NULL,
                PRIMARY KEY (tenant_id, code)
            );
            TRUNCATE ms_authors, ms_books, ms_editions;
        """);
        await conn.ExecuteAsync(
            "INSERT INTO ms_authors (id, name, email) VALUES (@Id, @Name, @Email)",
            new[]
            {
                new { Id = AuthorMoby,   Name = "Moby Dickens", Email = "moby@sea.org" },
                new { Id = AuthorOther,  Name = "Mob Adjacent", Email = "mob@sea.org" },
                new { Id = AuthorHidden, Name = "Moby Hidden",  Email = "hidden@sea.org" },
            });
        await conn.ExecuteAsync(
            "INSERT INTO ms_books (id, title, isbn) VALUES (@Id, @Title, @Isbn)",
            new[] { new { Id = BookMoby, Title = "Moby-Dick", Isbn = "978-0142437247" } });
        await conn.ExecuteAsync(
            "INSERT INTO ms_editions (tenant_id, code, label) VALUES (@TenantId, @Code, @Label)",
            new[] { new { TenantId = Tenant, Code = "COLL-1", Label = "Collector Edition" } });
    }
}
