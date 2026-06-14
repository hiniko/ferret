using System.Data.Common;
using Ferret.Abstractions;
using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Search;
using Ferret.Core.Backends.FullText;
using Ferret.Core.Engine;
using Ferret.Core.Engine.Reindex;
using Ferret.Core.IntegrationTests.Fixtures;
using Ferret.Hydration.Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.FullText;

[Collection("postgres")]
public class CrossEntitySearchEndToEndTests
{
    private readonly PostgresFixture _fx;

    public CrossEntitySearchEndToEndTests(PostgresFixture fx) => _fx = fx;

    // crm schema → exercises the cross-schema join path. No Table override so the
    // join hop table name (naming.TableName) and this entity's table agree on
    // "customers"; the [SearchableEntity] attribute supplies the join hop schema.
    [SearchableEntity(Schema = "crm")]
    public sealed class Customer
    {
        public Guid Id { get; init; }
        [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 2.0f)]
        public string Name { get; init; } = "";
    }

    public sealed class LineItem
    {
        public Guid Id { get; init; }
        public Guid OrderId { get; init; }
        [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 1.0f)]
        public string Sku { get; init; } = "";
    }

    [SearchableEntity(Table = "ce_orders")]
    [SearchGroup("content", FullTextConfig = "simple")]
    public sealed class Order : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 3.0f)]
        public string Title { get; init; } = "";

        public Guid CustomerId { get; init; }

        [SearchJoin]
        public Customer Customer { get; init; } = default!;

        [SearchJoin(ForeignKey = "order_id")]
        public IReadOnlyList<LineItem> Lines { get; init; } = [];
    }

    [Fact]
    public async Task Related_row_edits_refresh_owner_including_cross_schema()
    {
        var sp = BuildServices();
        var registry = sp.GetRequiredService<EntityRegistry>();
        var engine = sp.GetRequiredService<IFerretEngine>();
        var reindex = sp.GetRequiredService<IReindexRunner>();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        var ftOptions = sp.GetRequiredService<FullTextOptions>();
        var model = registry.Get<Order>();

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var line1 = Guid.NewGuid();

        await CreateSchemaAsync(conn, orderId, customerId, line1);
        await EmitFerretDdlAsync(conn, model, ftOptions);
        await BackfillAsync(conn, model, ftOptions);

        await using var session = NewSession(dialect);

        // 1. Search by the related customer name returns the owning order.
        (await SearchIds(engine, session, "Aurora")).Should().Contain(orderId);
        // owner-local title is searchable too
        (await SearchIds(engine, session, "Gizmo")).Should().Contain(orderId);
        // the seeded line sku is searchable via the 1:N join
        (await SearchIds(engine, session, "SKU-ALPHA")).Should().Contain(orderId);

        // 2. Update the customer name (cross-schema N:1). The change-tracking
        //    trigger must enqueue the owning order; the worker drains it.
        await Exec(conn, $"UPDATE crm.customers SET name = 'Borealis' WHERE id = '{customerId}';");
        (await PendingJobCount(conn, model.TableName, "content")).Should().BeGreaterThan(0);
        await reindex.DrainAsync(session, new ReindexDrainOptions { BatchSizeOverride = 500 }, CancellationToken.None);

        (await SearchIds(engine, session, "Borealis")).Should().Contain(orderId);
        (await SearchIds(engine, session, "Aurora")).Should().NotContain(orderId);

        // 3. Insert a new line item — its SKU becomes searchable on the owner.
        await Exec(conn,
            $"INSERT INTO line_items (id, order_id, sku) VALUES ('{Guid.NewGuid()}', '{orderId}', 'SKU-BETA');");
        (await PendingJobCount(conn, model.TableName, "content")).Should().BeGreaterThan(0);
        await reindex.DrainAsync(session, new ReindexDrainOptions { BatchSizeOverride = 500 }, CancellationToken.None);

        (await SearchIds(engine, session, "SKU-BETA")).Should().Contain(orderId);

        // 4. Delete the original line — its SKU must stop matching the owner.
        await Exec(conn, $"DELETE FROM line_items WHERE id = '{line1}';");
        (await PendingJobCount(conn, model.TableName, "content")).Should().BeGreaterThan(0);
        await reindex.DrainAsync(session, new ReindexDrainOptions { BatchSizeOverride = 500 }, CancellationToken.None);

        (await SearchIds(engine, session, "SKU-ALPHA")).Should().NotContain(orderId);
        (await SearchIds(engine, session, "SKU-BETA")).Should().Contain(orderId);
    }

    private static async Task<IReadOnlyList<Guid>> SearchIds(
        IFerretEngine engine, DapperSession session, string term)
    {
        var result = await engine.SearchOffsetAsync<Order, Guid>(session, new PagedQuery<Order, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = term,
            Limit = 50,
        });
        return result.Items.Select(o => o.Id).ToList();
    }

    private static async Task CreateSchemaAsync(NpgsqlConnection conn, Guid orderId, Guid customerId, Guid line1)
    {
        await Exec(conn, $"""
            DROP TABLE IF EXISTS ce_orders_search CASCADE;
            DROP TABLE IF EXISTS line_items CASCADE;
            DROP TABLE IF EXISTS ce_orders CASCADE;
            DROP SCHEMA IF EXISTS crm CASCADE;
            CREATE SCHEMA crm;

            CREATE TABLE crm.customers (
                id uuid PRIMARY KEY,
                name text NOT NULL
            );
            CREATE TABLE ce_orders (
                id uuid PRIMARY KEY,
                title text NOT NULL,
                customer_id uuid NOT NULL REFERENCES crm.customers(id)
            );
            CREATE TABLE line_items (
                id uuid PRIMARY KEY,
                order_id uuid NOT NULL REFERENCES ce_orders(id) ON DELETE CASCADE,
                sku text NOT NULL
            );

            INSERT INTO crm.customers (id, name) VALUES ('{customerId}', 'Aurora');
            INSERT INTO ce_orders (id, title, customer_id) VALUES ('{orderId}', 'Gizmo box', '{customerId}');
            INSERT INTO line_items (id, order_id, sku) VALUES ('{line1}', '{orderId}', 'SKU-ALPHA');
            """);
    }

    private static async Task EmitFerretDdlAsync(NpgsqlConnection conn, EntityModel model, FullTextOptions ftOptions)
    {
        var sidecar = FullTextSidecarNaming.TableName(model.TableName, ftOptions);
        var idColumn = model.KeyColumnName;

        await Exec(conn, FullTextDdlBuilder.CreateSidecarTable(
            sidecar, ftOptions.SidecarSchema, model.TableName, model.Schema, idColumn, "uuid"));

        foreach (var group in model.FullTextGroups)
        {
            var col = FullTextSidecarNaming.ColumnName(group.Name, ftOptions);
            await Exec(conn, FullTextDdlBuilder.AddGroupColumn(sidecar, ftOptions.SidecarSchema, col));
            await Exec(conn, FullTextDdlBuilder.CreateGroupIndex(
                sidecar, ftOptions.SidecarSchema, FullTextSidecarNaming.IndexName(sidecar, col), col));
        }

        // Owner-local sync trigger composes owner-local properties into the sidecar.
        await Exec(conn, FullTextDdlBuilder.CreateSyncFunctionAndTrigger(
            sidecar, ftOptions.SidecarSchema, model.TableName, model.Schema, idColumn,
            FullTextSidecarNaming.SyncFunctionName(model.TableName),
            FullTextSidecarNaming.SyncTriggerName(model.TableName),
            ftOptions.ColumnSuffix, model.FullTextGroups));

        await Exec(conn, FullTextDdlBuilder.EnsureReindexJobsTable());

        // One change-tracking trigger per distinct joined table.
        foreach (var (group, path) in JoinedTables(model))
        {
            var joinedTable = path.Hops[^1].TableName;
            var joinedSchema = path.Hops[^1].Schema;
            await Exec(conn, FullTextDdlBuilder.CreateChangeTrackingFunctionAndTrigger(
                joinedTable, joinedSchema,
                model.TableName, model.Schema,
                new[] { idColumn },
                path,
                FullTextSidecarNaming.ChangeTrackingFunctionName(model.TableName, joinedTable, joinedSchema),
                FullTextSidecarNaming.ChangeTrackingTriggerName(model.TableName, joinedTable, joinedSchema),
                model.TableName,
                group.Name));
        }
    }

    private static IEnumerable<(FullTextGroup Group, JoinPath Path)> JoinedTables(EntityModel model)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in model.FullTextGroups)
        foreach (var prop in group.Properties)
        {
            if (prop.Join is not { Hops.Count: > 0 } path) continue;
            var key = group.Name + "::" + path.Hops[^1].TableName;
            if (seen.Add(key))
                yield return (group, path);
        }
    }

    private static async Task BackfillAsync(NpgsqlConnection conn, EntityModel model, FullTextOptions ftOptions)
    {
        var sidecar = FullTextSidecarNaming.TableName(model.TableName, ftOptions);
        await Exec(conn, FullTextDdlBuilder.Backfill(
            sidecar, ftOptions.SidecarSchema, model.TableName, model.Schema,
            model.KeyColumnName, ftOptions.ColumnSuffix, model.FullTextGroups));
    }

    private static async Task<long> PendingJobCount(NpgsqlConnection conn, string entity, string group)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT count(*) FROM "{FullTextDdlBuilder.ReindexJobsTable}"
            WHERE "entity" = @entity AND "group_name" = @group AND "status" = 'pending';
            """;
        cmd.Parameters.AddWithValue("entity", entity);
        cmd.Parameters.AddWithValue("group", group);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private DapperSession NewSession(ISqlDialect dialect) =>
        new(ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_fx.ConnectionString)), dialect);

    private ServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts => opts
            .ScanAssembly(typeof(Order).Assembly)
            .UsePostgres()
            .UseFullTextSearch(ft => ft.DefaultConfig = "simple")
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
