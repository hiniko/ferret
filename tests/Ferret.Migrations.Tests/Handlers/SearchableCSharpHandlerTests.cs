using FluentAssertions;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Ferret.Migrations.Tests.Handlers;

public class SearchableCSharpHandlerTests
{
    [Fact]
    public void CanHandle_returns_true_only_for_known_operations()
    {
        var handler = new SearchableCSharpHandler();
        handler.CanHandle(new EnsurePgTrgmExtensionOperation()).Should().BeTrue();
        handler.CanHandle(new CreateSearchableIndexOperation
        {
            IndexName = "i", TableName = "t", ColumnName = "c", IndexSql = "x"
        }).Should().BeTrue();
        handler.CanHandle(new DropSearchableIndexOperation { IndexName = "i" }).Should().BeTrue();
        handler.CanHandle(new Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation()).Should().BeFalse();
    }

    [Fact]
    public void Generate_create_emits_Sql_call_with_suppressTransaction()
    {
        var handler = new SearchableCSharpHandler();
        var builder = new IndentedStringBuilder();
        handler.Generate(new CreateSearchableIndexOperation
        {
            IndexName = "ix_widgets_name_gist_trgm",
            TableName = "widgets",
            ColumnName = "name",
            IndexSql = "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"ix_widgets_name_gist_trgm\" ON \"widgets\" USING gist ((\"name\"::text) gist_trgm_ops);",
        }, builder);

        var output = builder.ToString();
        output.Should().Contain("migrationBuilder.Sql(");
        output.Should().Contain("suppressTransaction: true");
        output.Should().Contain("CREATE INDEX CONCURRENTLY IF NOT EXISTS");
        output.Should().Contain("gist_trgm_ops");
    }

    [Fact]
    public void Generate_drop_emits_DROP_INDEX_CONCURRENTLY()
    {
        var handler = new SearchableCSharpHandler();
        var builder = new IndentedStringBuilder();
        handler.Generate(new DropSearchableIndexOperation { IndexName = "ix_widgets_name_gist_trgm" }, builder);

        var output = builder.ToString();
        output.Should().Contain("migrationBuilder.Sql(");
        output.Should().Contain("DROP INDEX CONCURRENTLY IF EXISTS");
        output.Should().Contain("\"ix_widgets_name_gist_trgm\"");
        output.Should().Contain("suppressTransaction: true");
    }

    [Fact]
    public void Generate_extension_emits_CREATE_EXTENSION()
    {
        var handler = new SearchableCSharpHandler();
        var builder = new IndentedStringBuilder();
        handler.Generate(new EnsurePgTrgmExtensionOperation(), builder);

        var output = builder.ToString();
        output.Should().Contain("migrationBuilder.Sql(");
        output.Should().Contain("CREATE EXTENSION IF NOT EXISTS \"pg_trgm\"");
    }
}
