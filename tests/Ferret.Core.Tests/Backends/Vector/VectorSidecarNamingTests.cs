using Ferret.Core.Backends.Vector;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Backends.Vector;

public class VectorSidecarNamingTests
{
    private static readonly VectorOptions Opts = new();

    [Fact]
    public void Sidecar_table_appends_suffix()
        => VectorSidecarNaming.TableName("products", Opts).Should().Be("products_vec");

    [Fact]
    public void Index_name_is_stable()
        => VectorSidecarNaming.IndexName("products_vec", "content_embedding_v1")
            .Should().Be("ix_products_vec_content_embedding_v1_hnsw");

    [Fact]
    public void Versioned_column_name_appends_v_suffix()
    {
        var opts = new VectorOptions(); // ColumnSuffix = "_embedding"
        VectorSidecarNaming.ColumnName("title", opts, version: 1).Should().Be("title_embedding_v1");
        VectorSidecarNaming.CurrentVersion.Should().Be(1);
    }
}
