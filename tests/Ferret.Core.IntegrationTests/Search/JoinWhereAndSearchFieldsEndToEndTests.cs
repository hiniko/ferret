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
public class JoinWhereAndSearchFieldsEndToEndTests : IAsyncLifetime
{
    private readonly PostgresFixture _fx;

    public JoinWhereAndSearchFieldsEndToEndTests(PostgresFixture fx) => _fx = fx;

    [SearchableEntity(Table = "sj_products")]
    public sealed class SjProduct : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Title { get; init; } = "";
        [Searchable] public string Sku { get; init; } = "";

        [SearchJoin(ForeignKey = "product_id", Where = "{c}.deleted_at IS NULL AND {c}.hidden = false")]
        public IReadOnlyList<SjVariant> Variants { get; init; } = [];
    }

    public sealed class SjVariant
    {
        public Guid Id { get; init; }
        [Searchable] public string Label { get; init; } = "";
    }

    private static readonly Guid PWithLive    = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly Guid PWithDeleted = Guid.Parse("00000003-0000-0000-0000-000000000002");
    private static readonly Guid PWithHidden  = Guid.Parse("00000003-0000-0000-0000-000000000003");

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            DROP TABLE IF EXISTS sj_variants;
            DROP TABLE IF EXISTS sj_products;
            CREATE TABLE sj_products (
                id uuid PRIMARY KEY,
                title text NOT NULL,
                sku text NOT NULL
            );
            CREATE TABLE sj_variants (
                id uuid PRIMARY KEY,
                product_id uuid NOT NULL REFERENCES sj_products(id),
                label text NOT NULL,
                deleted_at timestamptz NULL,
                hidden boolean NOT NULL DEFAULT false
            );
        """);

        await conn.ExecuteAsync(
            "INSERT INTO sj_products (id, title, sku) VALUES (@Id, @Title, @Sku)",
            new[]
            {
                new { Id = PWithLive,    Title = "Alpha Coat", Sku = "AC-1" },
                new { Id = PWithDeleted, Title = "Beta Coat",  Sku = "BC-1" },
                new { Id = PWithHidden,  Title = "Gamma Coat", Sku = "GC-1" },
            });
        await conn.ExecuteAsync(
            "INSERT INTO sj_variants (id, product_id, label, deleted_at, hidden) VALUES (@Id, @Pid, @Label, @DeletedAt, @Hidden)",
            new object[]
            {
                new { Id = Guid.NewGuid(), Pid = PWithLive,    Label = "zephyr special", DeletedAt = (DateTime?)null,       Hidden = false },
                new { Id = Guid.NewGuid(), Pid = PWithDeleted, Label = "zephyr special", DeletedAt = (DateTime?)DateTime.UtcNow, Hidden = false },
                new { Id = Guid.NewGuid(), Pid = PWithHidden,  Label = "zephyr special", DeletedAt = (DateTime?)null,       Hidden = true },
            });
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("DROP TABLE IF EXISTS sj_variants; DROP TABLE IF EXISTS sj_products");
    }

    [Fact]
    public async Task Join_where_excludes_deleted_and_hidden_children_from_ranking()
    {
        var engine = BuildEngine();

        var result = await engine.SearchOffsetAsync<SjProduct, Guid>(NewSession(), new PagedQuery<SjProduct, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "zephyr",
            Limit = 25,
        });

        result.Items.Select(p => p.Id).Should().BeEquivalentTo([PWithLive]);
    }

    [Fact]
    public async Task SearchFields_restricts_to_named_column()
    {
        var engine = BuildEngine();

        // "coat" matches Title on all three products, but restricting to Sku must return nothing.
        var restricted = await engine.SearchOffsetAsync<SjProduct, Guid>(NewSession(), new PagedQuery<SjProduct, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "coat",
            SearchFields = ["sku"],
            Limit = 25,
        });
        restricted.Items.Should().BeEmpty();

        var unrestricted = await engine.SearchOffsetAsync<SjProduct, Guid>(NewSession(), new PagedQuery<SjProduct, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "coat",
            SearchFields = ["title"],
            Limit = 25,
        });
        unrestricted.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task SearchFields_qualified_form_targets_joined_column()
    {
        var engine = BuildEngine();

        var result = await engine.SearchOffsetAsync<SjProduct, Guid>(NewSession(), new PagedQuery<SjProduct, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "zephyr",
            SearchFields = ["sj_variants.label"],
            Limit = 25,
        });

        result.Items.Select(p => p.Id).Should().BeEquivalentTo([PWithLive]);
    }

    [Fact]
    public async Task SearchFields_with_only_unknown_names_returns_empty()
    {
        var engine = BuildEngine();

        var result = await engine.SearchOffsetAsync<SjProduct, Guid>(NewSession(), new PagedQuery<SjProduct, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "coat",
            SearchFields = ["no_such_field"],
            Limit = 25,
        });

        result.Items.Should().BeEmpty();
    }

    private DapperSession NewSession()
    {
        var dialect = new Core.Sql.PostgresDialect();
        return new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_fx.ConnectionString)),
            dialect);
    }

    private IFerretEngine BuildEngine()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts => opts
            .ScanAssembly(typeof(SjProduct).Assembly)
            .UseTrigramSearch()
            .UseDapperHydration());
        return sc.BuildServiceProvider().GetRequiredService<IFerretEngine>();
    }
}
