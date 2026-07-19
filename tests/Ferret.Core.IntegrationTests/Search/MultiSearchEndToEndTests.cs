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
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
        [Filterable] public string Email { get; init; } = "";
        public bool Hidden { get; init; }
    }

    [SearchableEntity(Table = "ms_books")]
    public sealed class Book : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Title { get; init; } = "";
        public string Isbn { get; init; } = "";
    }

    [SearchableEntity(Table = "ms_editions", KeyProperties = [nameof(TenantId), nameof(Code)])]
    public sealed class Edition
    {
        public Guid TenantId { get; init; }
        public string Code { get; init; } = "";
        [Searchable] public string Label { get; init; } = "";
    }

    [SearchableEntity(Table = "ms_vdocs")]
    public sealed class VectorDoc : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable(Backend = SearchBackend.Vector, EmbeddingDimensions = 8)] public string Content { get; init; } = "";
    }

    public sealed class AuthorHit : MultiSearchHit
    {
        public string Name { get; set; } = "";
        public string? Email { get; set; }
    }

    public sealed class BookHit : MultiSearchHit
    {
        public string Title { get; set; } = "";
        public string Isbn { get; set; } = "";
        public string Badge { get; set; } = "";
    }

    public sealed class EditionHit : MultiSearchHit
    {
        public string Label { get; set; } = "";
    }

    public sealed class VectorDocHit : MultiSearchHit
    {
        public string Content { get; set; } = "";
    }

    private static readonly Guid AuthorMoby   = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly Guid AuthorOther  = Guid.Parse("00000003-0000-0000-0000-000000000002");
    private static readonly Guid AuthorHidden = Guid.Parse("00000003-0000-0000-0000-000000000003");
    private static readonly Guid BookMoby     = Guid.Parse("00000003-0000-0000-0000-000000000011");
    private static readonly Guid Tenant       = Guid.Parse("00000003-0000-0000-0000-000000000021");

    [Fact]
    public async Task Grouped_hits_are_typed_stamped_and_capped()
    {
        await SeedAsync();
        var (engine, session) = Engine();

        var result = await engine.MultiSearchAsync(session, q => q
            .Search("moby")
            .PerEntityLimit(5)
            .Entity<Author, AuthorHit>(a => new AuthorHit { Name = a.Name, Email = a.Email })
            .Entity<Book, BookHit>(b => new BookHit { Title = b.Title, Isbn = b.Isbn, Badge = "book" }));

        result.Groups.Should().HaveCount(2);
        result.Groups[0].EntityType.Should().Be("ms_authors");
        result.Groups[1].EntityType.Should().Be("ms_books");

        var author = result.Groups[0].Hits.Should().NotBeEmpty()
            .And.AllBeOfType<AuthorHit>().Subject.First();
        author.Name.Should().Be("Moby Dickens");
        author.Email.Should().Be("moby@sea.org");
        author.EntityType.Should().Be("ms_authors");
        author.Key.Should().Be(AuthorMoby.ToString("N"));
        // Trigram score is a distance: 0.0 is a perfect word match.
        author.Score.Should().BeInRange(0.0, 1.0);

        var book = (BookHit)result.Groups[1].Hits.First();
        book.Title.Should().Be("Moby-Dick");
        book.Badge.Should().Be("book");
    }

    [Fact]
    public async Task Best_match_first_within_group()
    {
        await SeedAsync();
        var (engine, session) = Engine();

        var result = await engine.MultiSearchAsync(session, q => q
            .Search("moby dickens")
            .Entity<Author, AuthorHit>(a => new AuthorHit { Name = a.Name }));

        var names = result.Groups[0].Hits.Cast<AuthorHit>().Select(h => h.Name).ToList();
        names.First().Should().Be("Moby Dickens");
    }

    [Fact]
    public async Task Per_entity_filter_excludes_matching_row()
    {
        await SeedAsync();
        var (engine, session) = Engine();

        var result = await engine.MultiSearchAsync(session, q => q
            .Search("moby")
            .Entity<Author, AuthorHit>(a => new AuthorHit { Name = a.Name },
                o => o.Filter(new FilterClause
                {
                    Field = "Email", Operator = FilterOperator.NotEquals, Value = "moby@sea.org",
                })));

        result.Groups[0].Hits.Cast<AuthorHit>().Should().NotContain(h => h.Name == "Moby Dickens");
    }

    [Fact]
    public async Task No_matches_yields_empty_groups()
    {
        await SeedAsync();
        var (engine, session) = Engine();

        var result = await engine.MultiSearchAsync(session, q => q
            .Search("zzzzqqqq")
            .Entity<Author, AuthorHit>(a => new AuthorHit { Name = a.Name })
            .Entity<Book, BookHit>(b => new BookHit { Title = b.Title }));

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
            .Entity<Author, AuthorHit>(a => new AuthorHit { Name = a.Name })
            .Entity<Book, BookHit>(b => new BookHit { Title = b.Title }));

        result.Groups.Should().OnlyContain(g => g.Hits.Count <= 1);
        result.Groups[0].Hits.Should().HaveCount(1);
    }

    [Fact]
    public async Task Composite_key_is_string_encoded()
    {
        await SeedAsync();
        var (engine, session) = Engine();

        var result = await engine.MultiSearchAsync(session, q => q
            .Search("collector")
            .Entity<Edition, EditionHit>(e => new EditionHit { Label = e.Label }));

        var hit = result.Groups[0].Hits.Should().ContainSingle().Subject;
        hit.Key.Should().Be($"{Tenant:N}\u001fCOLL-1");
    }

    [Fact]
    public async Task Types_subsets_groups_and_unknown_type_throws()
    {
        await SeedAsync();
        var (engine, session) = Engine();

        var subset = await engine.MultiSearchAsync(session, q => q
            .Search("moby")
            .Types("ms_books")
            .Entity<Author, AuthorHit>(a => new AuthorHit { Name = a.Name })
            .Entity<Book, BookHit>(b => new BookHit { Title = b.Title }));

        subset.Groups.Should().ContainSingle().Which.EntityType.Should().Be("ms_books");

        await FluentActions.Awaiting(() => engine.MultiSearchAsync(session, q => q
                .Search("moby")
                .Types("nope")
                .Entity<Author, AuthorHit>(a => new AuthorHit { Name = a.Name })))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*nope*ms_authors*");
    }

    [Fact]
    public async Task Vector_entity_is_rejected()
    {
        var (engine, session) = Engine();

        await FluentActions.Awaiting(() => engine.MultiSearchAsync(session, q => q
                .Search("moby")
                .Entity<VectorDoc, VectorDocHit>(v => new VectorDocHit { Content = v.Content })))
            .Should().ThrowAsync<NotSupportedException>().WithMessage("*ms_vdocs*");
    }

    private (IFerretEngine Engine, DapperSession Session) Engine()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts => opts
            .ScanAssembly(typeof(Author).Assembly)
            .UseTrigramSearch()
            .UseDapperHydration());
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
