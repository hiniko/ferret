using Ferret.Abstractions.Search;
using Ferret.Core.Backends.FullText;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Backends.FullText;

public class FullTextChangeTrackingDdlTests
{
    private static JoinPath DirectOneToMany() => new()
    {
        Hops =
        [
            new JoinHop
            {
                TableName = "order_lines", TableAlias = "ol1",
                ForeignKeyColumn = "order_id", EntityType = typeof(object),
                Cardinality = JoinCardinality.OneToMany, ForeignKeyOwningSide = false,
            },
        ],
    };

    private static JoinPath ManyToOne() => new()
    {
        Hops =
        [
            new JoinHop
            {
                TableName = "customers", TableAlias = "cust1",
                ForeignKeyColumn = "customer_id", EntityType = typeof(object),
                Cardinality = JoinCardinality.ManyToOne, ForeignKeyOwningSide = true,
            },
        ],
    };

    private static JoinPath TwoHop() => new()
    {
        // owner orders -> order_lines (1:N) -> products (N:1)
        Hops =
        [
            new JoinHop
            {
                TableName = "order_lines", TableAlias = "ol1",
                ForeignKeyColumn = "order_id", EntityType = typeof(object),
                Cardinality = JoinCardinality.OneToMany, ForeignKeyOwningSide = false,
            },
            new JoinHop
            {
                TableName = "products", TableAlias = "p1",
                ForeignKeyColumn = "product_id", EntityType = typeof(object),
                Cardinality = JoinCardinality.ManyToOne, ForeignKeyOwningSide = true,
            },
        ],
    };

    [Fact]
    public void Direct_one_to_many_resolves_owner_via_fk_column_on_new_and_old()
    {
        var sql = FullTextDdlBuilder.CreateChangeTrackingFunctionAndTrigger(
            joinedTable: "order_lines", joinedSchema: null,
            ownerTable: "orders", ownerSchema: null,
            ownerKeyColumns: new[] { "id" },
            joinPath: DirectOneToMany(),
            functionName: "orders__order_lines_ct",
            triggerName: "orders__order_lines_ct_t",
            entityName: "Order",
            groupName: "content");

        // schema-qualified table the trigger is attached to
        sql.Should().Contain("ON \"order_lines\"");
        sql.Should().Contain("AFTER INSERT OR UPDATE OR DELETE");
        // FK column on the joined row gives the owner key directly
        sql.Should().Contain("NEW.\"order_id\"");
        sql.Should().Contain("OLD.\"order_id\"");
        // enqueue into reindex jobs with owner entity + group
        sql.Should().Contain("INSERT INTO \"ferret_reindex_jobs\"");
        sql.Should().Contain("'Order'");
        sql.Should().Contain("'content'");
    }

    [Fact]
    public void Many_to_one_resolves_owners_where_owner_fk_equals_new_id()
    {
        var sql = FullTextDdlBuilder.CreateChangeTrackingFunctionAndTrigger(
            joinedTable: "customers", joinedSchema: null,
            ownerTable: "orders", ownerSchema: null,
            ownerKeyColumns: new[] { "id" },
            joinPath: ManyToOne(),
            functionName: "orders__customers_ct",
            triggerName: "orders__customers_ct_t",
            entityName: "Order",
            groupName: "content");

        // walk back to owner by selecting owner ids where owner.customer_id = NEW.id
        sql.Should().Contain("SELECT DISTINCT \"o\".\"id\" FROM \"orders\" \"o\"");
        sql.Should().Contain("WHERE \"o\".\"customer_id\" = NEW.\"id\"");
        sql.Should().Contain("WHERE \"o\".\"customer_id\" = OLD.\"id\"");
        // owner-id enqueues are de-duplicated against pending jobs
        sql.Should().Contain("WHERE NOT EXISTS (");
        sql.Should().Contain("AND \"status\" = 'pending' AND \"last_id\" = ");
    }

    [Fact]
    public void Many_to_one_uses_referenced_entity_key_column_not_hardcoded_id()
    {
        // Owner orders.warehouse_id -> warehouses, whose PK column is "code", not "id".
        var path = new JoinPath
        {
            Hops =
            [
                new JoinHop
                {
                    TableName = "warehouses", TableAlias = "wh1",
                    ForeignKeyColumn = "warehouse_id", EntityType = typeof(object),
                    Cardinality = JoinCardinality.ManyToOne, ForeignKeyOwningSide = true,
                    ReferencedKeyColumn = "code",
                },
            ],
        };

        var sql = FullTextDdlBuilder.CreateChangeTrackingFunctionAndTrigger(
            joinedTable: "warehouses", joinedSchema: null,
            ownerTable: "orders", ownerSchema: null,
            ownerKeyColumns: new[] { "id" },
            joinPath: path,
            functionName: "orders__warehouses_ct",
            triggerName: "orders__warehouses_ct_t",
            entityName: "Order",
            groupName: "content");

        // owner.warehouse_id matches the changed warehouse row's key column (code), not id
        sql.Should().Contain("\"warehouse_id\" = NEW.\"code\"");
        sql.Should().Contain("\"warehouse_id\" = OLD.\"code\"");
        sql.Should().NotContain("NEW.\"id\"");
        sql.Should().NotContain("OLD.\"id\"");
    }

    [Fact]
    public void Two_hop_resolves_owner_transitively()
    {
        var sql = FullTextDdlBuilder.CreateChangeTrackingFunctionAndTrigger(
            joinedTable: "products", joinedSchema: null,
            ownerTable: "orders", ownerSchema: null,
            ownerKeyColumns: new[] { "id" },
            joinPath: TwoHop(),
            functionName: "orders__products_ct",
            triggerName: "orders__products_ct_t",
            entityName: "Order",
            groupName: "content");

        sql.Should().Contain("ON \"products\"");
        // resolves through intermediate order_lines back to orders, with the
        // intermediate join keyed on the owner FK and the changed product row
        // linked via the order_lines product FK.
        sql.Should().Contain("SELECT DISTINCT \"o\".\"id\" FROM \"orders\" \"o\"");
        sql.Should().Contain("JOIN \"order_lines\" \"h0\" ON \"h0\".\"order_id\" = \"o\".\"id\"");
        sql.Should().Contain("WHERE \"h0\".\"product_id\" = NEW.\"id\"");
        sql.Should().Contain("WHERE \"h0\".\"product_id\" = OLD.\"id\"");
        // transitive enqueue is de-duplicated
        sql.Should().Contain("WHERE NOT EXISTS (");
    }

    [Fact]
    public void Schema_qualified_tables_are_qualified()
    {
        var path = new JoinPath
        {
            Hops =
            [
                new JoinHop
                {
                    TableName = "order_lines", TableAlias = "ol1",
                    ForeignKeyColumn = "order_id", EntityType = typeof(object),
                    Cardinality = JoinCardinality.OneToMany, ForeignKeyOwningSide = false,
                    Schema = "sales",
                },
            ],
        };

        var sql = FullTextDdlBuilder.CreateChangeTrackingFunctionAndTrigger(
            joinedTable: "order_lines", joinedSchema: "sales",
            ownerTable: "orders", ownerSchema: "app",
            ownerKeyColumns: new[] { "id" },
            joinPath: path,
            functionName: "orders__order_lines_ct",
            triggerName: "orders__order_lines_ct_t",
            entityName: "Order",
            groupName: "content");

        sql.Should().Contain("ON \"sales\".\"order_lines\"");
        sql.Should().Contain("CREATE OR REPLACE FUNCTION \"orders__order_lines_ct\"()");
    }

    [Fact]
    public void Insert_update_uses_new_delete_uses_old()
    {
        var sql = FullTextDdlBuilder.CreateChangeTrackingFunctionAndTrigger(
            joinedTable: "order_lines", joinedSchema: null,
            ownerTable: "orders", ownerSchema: null,
            ownerKeyColumns: new[] { "id" },
            joinPath: DirectOneToMany(),
            functionName: "orders__order_lines_ct",
            triggerName: "orders__order_lines_ct_t",
            entityName: "Order",
            groupName: "content");

        sql.Should().Contain("TG_OP = 'DELETE'");
        sql.Should().Contain("NEW.\"order_id\"");
        sql.Should().Contain("OLD.\"order_id\"");
    }

    [Fact]
    public void Drop_emits_drop_trigger_then_drop_function()
    {
        var sql = FullTextDdlBuilder.DropChangeTrackingFunctionAndTrigger(
            joinedTable: "order_lines", joinedSchema: null,
            functionName: "orders__order_lines_ct",
            triggerName: "orders__order_lines_ct_t");

        var dropTrigger = sql.IndexOf("DROP TRIGGER", System.StringComparison.Ordinal);
        var dropFunction = sql.IndexOf("DROP FUNCTION", System.StringComparison.Ordinal);
        dropTrigger.Should().BeGreaterThanOrEqualTo(0);
        dropFunction.Should().BeGreaterThan(dropTrigger);
        sql.Should().Contain("DROP TRIGGER IF EXISTS \"orders__order_lines_ct_t\" ON \"order_lines\"");
        sql.Should().Contain("DROP FUNCTION IF EXISTS \"orders__order_lines_ct\"()");
    }

    [Fact]
    public void Composite_owner_key_escapes_each_part_before_pipe_join()
    {
        var sql = FullTextDdlBuilder.CreateChangeTrackingFunctionAndTrigger(
            joinedTable: "customers", joinedSchema: null,
            ownerTable: "orders", ownerSchema: null,
            ownerKeyColumns: new[] { "tenant_id", "id" },
            joinPath: ManyToOne(),
            functionName: "orders__customers_ct",
            triggerName: "orders__customers_ct_t",
            entityName: "Order",
            groupName: "content");

        // The composite key must escape '\' -> '\\' and '|' -> '\|' per part, byte-identical
        // to ReindexJobProcessor.EncodeCompositeKey, then '|'-join via concat_ws.
        sql.Should().Contain(@"concat_ws('|', replace(replace(_owner_key_0::text, '\', '\\'), '|', '\|'), replace(replace(_owner_key_1::text, '\', '\\'), '|', '\|'))");
    }

    [Fact]
    public void Single_owner_key_is_not_escaped()
    {
        var sql = FullTextDdlBuilder.CreateChangeTrackingFunctionAndTrigger(
            joinedTable: "customers", joinedSchema: null,
            ownerTable: "orders", ownerSchema: null,
            ownerKeyColumns: new[] { "id" },
            joinPath: ManyToOne(),
            functionName: "orders__customers_ct",
            triggerName: "orders__customers_ct_t",
            entityName: "Order",
            groupName: "content");

        sql.Should().NotContain("concat_ws");
        sql.Should().NotContain(@"replace(replace(");
    }

    [Fact]
    public void Names_are_deterministic()
    {
        var n1 = FullTextSidecarNaming.ChangeTrackingFunctionName("orders", "order_lines");
        var n2 = FullTextSidecarNaming.ChangeTrackingFunctionName("orders", "order_lines");
        n1.Should().Be(n2);
        FullTextSidecarNaming.ChangeTrackingTriggerName("orders", "order_lines")
            .Should().StartWith(n1);
    }

    [Fact]
    public void Names_distinguish_same_table_in_different_schemas()
    {
        var sales = FullTextSidecarNaming.ChangeTrackingFunctionName("orders", "items", "sales");
        var billing = FullTextSidecarNaming.ChangeTrackingFunctionName("orders", "items", "billing");
        var unqualified = FullTextSidecarNaming.ChangeTrackingFunctionName("orders", "items");

        sales.Should().NotBe(billing);
        sales.Should().NotBe(unqualified);
        FullTextSidecarNaming.ChangeTrackingTriggerName("orders", "items", "sales")
            .Should().NotBe(FullTextSidecarNaming.ChangeTrackingTriggerName("orders", "items", "billing"));
    }
}
