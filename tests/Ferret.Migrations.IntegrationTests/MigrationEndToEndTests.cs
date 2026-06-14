using Dapper;
using Ferret.Core.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Ferret.Migrations.IntegrationTests;

[Collection("postgres")]
public class MigrationEndToEndTests
{
    private readonly PostgresFixture _fx;

    public MigrationEndToEndTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Diff_emits_pg_trgm_extension_and_gist_indexes_then_DDL_creates_them()
    {
        // 1. Reset schema — drop existing widgets_mig table + indexes from prior runs
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("DROP TABLE IF EXISTS widgets_mig CASCADE");
        await conn.ExecuteAsync("DROP INDEX IF EXISTS ix_widgets_mig_name_gist_trgm");
        await conn.ExecuteAsync("DROP INDEX IF EXISTS ix_widgets_mig_sku_gist_trgm");
        await conn.ExecuteAsync(@"CREATE TABLE widgets_mig (
            id uuid PRIMARY KEY,
            name text NOT NULL,
            sku text NOT NULL
        )");

        // 2. Build a DbContext + design-time service container
        var dbOpts = new DbContextOptionsBuilder<MigDbContext>()
            .UseNpgsql(_fx.ConnectionString)
            .EnableServiceProviderCaching(false)
            .Options;
        await using var ctx = new MigDbContext(dbOpts);

        var designServices = new ServiceCollection();
        designServices.AddEntityFrameworkDesignTimeServices();
        designServices.AddDbContextDesignTimeServices(ctx);
        new MigDesignTimeServices().ConfigureDesignTimeServices(designServices);
        var designProvider = designServices.BuildServiceProvider();

        // 3. Run the wrapped differ — source is empty, target is the model
        var differ = designProvider.GetRequiredService<IMigrationsModelDiffer>();
        var ops = differ.GetDifferences(
            source: null,
            target: ctx.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        ops.OfType<EnsurePgTrgmExtensionOperation>().Should().HaveCount(1);
        ops.OfType<CreateSearchableIndexOperation>().Should().HaveCount(2);

        // 4. Apply the operations directly. CREATE INDEX CONCURRENTLY can't run inside
        //    a transaction, so we issue each statement on its own command (no NpgsqlBatch
        //    which wraps in a transaction by default).
        foreach (var op in ops)
        {
            string? sql = op switch
            {
                EnsurePgTrgmExtensionOperation ext => $"CREATE EXTENSION IF NOT EXISTS \"{ext.ExtensionName}\";",
                CreateSearchableIndexOperation create => create.IndexSql,
                DropSearchableIndexOperation drop => $"DROP INDEX CONCURRENTLY IF EXISTS \"{drop.IndexName}\";",
                _ => null,
            };
            if (sql is null) continue;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        // 5. Verify in Postgres
        var hasExtension = await conn.QuerySingleAsync<int>(
            "SELECT count(*) FROM pg_extension WHERE extname = 'pg_trgm'");
        hasExtension.Should().Be(1);

        var indexes = (await conn.QueryAsync<string>(@"
            SELECT indexname
            FROM pg_indexes
            WHERE tablename = 'widgets_mig'
              AND indexname LIKE 'ix_widgets_mig_%_gist_trgm'
            ORDER BY indexname")).ToList();

        indexes.Should().BeEquivalentTo(new[]
        {
            "ix_widgets_mig_name_gist_trgm",
            "ix_widgets_mig_sku_gist_trgm",
        });
    }
}
