using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Ferret.Migrations.Tests.Operations;

public class OperationShapeTests
{
    [Fact]
    public void EnsurePgTrgmExtensionOperation_defaults_extension_name()
    {
        var op = new EnsurePgTrgmExtensionOperation();
        op.ExtensionName.Should().Be("pg_trgm");
        op.Should().BeAssignableTo<MigrationOperation>();
    }

    [Fact]
    public void CreateSearchableIndexOperation_carries_required_fields()
    {
        var op = new CreateSearchableIndexOperation
        {
            IndexName = "ix_widgets_name_gist_trgm",
            TableName = "widgets",
            ColumnName = "name",
            IndexSql = "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"ix_widgets_name_gist_trgm\" ON \"widgets\" USING gist ((\"name\"::text) gist_trgm_ops);",
        };
        op.IndexName.Should().Be("ix_widgets_name_gist_trgm");
        op.TableName.Should().Be("widgets");
        op.ColumnName.Should().Be("name");
        op.IndexSql.Should().Contain("gist_trgm_ops");
    }

    [Fact]
    public void DropSearchableIndexOperation_carries_index_name()
    {
        var op = new DropSearchableIndexOperation { IndexName = "ix_widgets_name_gist_trgm" };
        op.IndexName.Should().Be("ix_widgets_name_gist_trgm");
    }
}
