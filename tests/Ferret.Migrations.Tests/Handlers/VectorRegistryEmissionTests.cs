using EntityFrameworkCore.ExtensibleMigrations;
using Ferret.Migrations.Handlers;
using Ferret.Migrations.Operations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ferret.Migrations.Tests.Handlers;

public class VectorRegistryEmissionTests
{
    [Fact]
    public void CSharp_handler_emits_registry_table_sql()
    {
        var handler = new VectorCSharpHandler();
        var op = new EnsureVectorVersionRegistryOperation { Schema = null };

        handler.CanHandle(op).Should().BeTrue();

        var sb = new IndentedStringBuilder();
        handler.Generate(op, sb);

        sb.ToString().Should().Contain("ferret_vector_versions");
    }

    [Fact]
    public void CSharp_handler_versions_the_created_column()
    {
        var handler = new VectorCSharpHandler();
        var op = new CreateVectorIndexOperation
        {
            Entity = "Doc",
            SidecarTable = "docs_vec",
            SidecarSchema = null,
            SourceTable = "docs",
            SourceSchema = null,
            IdColumn = "id",
            ColumnSuffix = "_embedding",
            Group = new Ferret.Abstractions.Search.VectorGroup
            {
                Name = "title",
                Dimensions = 768,
                Properties = new[]
                {
                    new Ferret.Abstractions.Search.VectorGroupProperty
                    {
                        PropertyName = "Title", ColumnName = "title", EmbeddingSource = "Title",
                    },
                },
            },
            HnswM = 16,
            HnswEfConstruction = 64,
            ConcurrentBatchSize = 1000,
        };

        var sb = new IndentedStringBuilder();
        handler.Generate(op, sb);

        sb.ToString().Should().Contain("title_embedding_v1");
    }
}
