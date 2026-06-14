using Ferret.Abstractions.Search;
using Ferret.Migrations.Handlers;
using Ferret.Migrations.Operations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Ferret.Migrations.Tests.Handlers;

public class VectorCSharpHandlerTests
{
    private static string Emit(MigrationOperation op)
    {
        var b = new IndentedStringBuilder();
        new VectorCSharpHandler().Generate(op, b);
        return b.ToString();
    }

    [Fact]
    public void Ensure_extension_emits_create_extension()
        => Emit(new EnsurePgvectorExtensionOperation()).Should().Contain("CREATE EXTENSION IF NOT EXISTS");

    [Fact]
    public void Create_index_emits_column_and_hnsw_index()
    {
        var g = new VectorGroup { Name = "content", Dimensions = 8, Properties = [] };
        var output = Emit(new CreateVectorIndexOperation
        {
            Entity = "Product", SidecarTable = "products_vec", SidecarSchema = null,
            SourceTable = "products", SourceSchema = null, IdColumn = "id", ColumnSuffix = "_embedding",
            Group = g, HnswM = 16, HnswEfConstruction = 64,
        });
        output.Should().Contain("content_embedding_v1");
        output.Should().Contain("vector(8)");
        output.Should().Contain("hnsw");
        output.Should().Contain("vector_cosine_ops");
        // v1: vector backfill is explicit-only — migration must not enqueue a reindex job.
        output.Should().NotContain("ferret_reindex_jobs");
    }
}
