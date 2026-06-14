using Ferret.Core.Backends.Vector;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Backends.Vector;

public class VectorDdlBuilderTests
{
    [Fact]
    public void EnsureExtension_creates_vector()
        => VectorDdlBuilder.EnsureExtension().Should().Contain("CREATE EXTENSION IF NOT EXISTS \"vector\"");

    [Fact]
    public void CreateSidecarTable_has_pk_fk_and_updated_at()
    {
        var sql = VectorDdlBuilder.CreateSidecarTable("products_vec", null, "products", null, "id", "uuid");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS \"products_vec\"");
        sql.Should().Contain("\"id\" uuid PRIMARY KEY");
        sql.Should().Contain("REFERENCES \"products\" (\"id\") ON DELETE CASCADE");
        sql.Should().Contain("\"updated_at\" timestamptz NOT NULL DEFAULT now()");
    }

    [Fact]
    public void AddGroupColumn_emits_vector_of_dimension()
        => VectorDdlBuilder.AddGroupColumn("products_vec", null, "content_embedding", 1536)
            .Should().Contain("ADD COLUMN IF NOT EXISTS \"content_embedding\" vector(1536)");

    [Fact]
    public void CreateGroupIndex_emits_hnsw_cosine_with_build_params()
    {
        var sql = VectorDdlBuilder.CreateGroupIndex("products_vec", null, "ix_products_vec_content_embedding_hnsw", "content_embedding", m: 16, efConstruction: 64);
        sql.Should().Contain("USING hnsw (\"content_embedding\" vector_cosine_ops)");
        sql.Should().Contain("WITH (m = 16, ef_construction = 64)");
    }

    [Fact]
    public void CreateVersionRegistry_emits_idempotent_table_with_expected_columns()
    {
        var sql = VectorDdlBuilder.CreateVersionRegistry(schema: null);

        sql.Should().Contain("CREATE TABLE IF NOT EXISTS \"ferret_vector_versions\"");
        sql.Should().Contain("\"version_id\" bigserial PRIMARY KEY");
        sql.Should().Contain("\"entity\" text NOT NULL");
        sql.Should().Contain("\"group_name\" text NOT NULL");
        sql.Should().Contain("\"model\" text NOT NULL");
        sql.Should().Contain("\"dimensions\" integer NOT NULL");
        sql.Should().Contain("\"column_name\" text NOT NULL");
        sql.Should().Contain("\"status\" text NOT NULL");
        sql.Should().Contain("\"created_at\" timestamptz NOT NULL DEFAULT now()");
        sql.Should().Contain("CONSTRAINT \"uq_ferret_vector_versions\" UNIQUE (\"entity\", \"group_name\", \"version_id\")");
        sql.Should().Contain("CREATE UNIQUE INDEX IF NOT EXISTS \"uq_ferret_vector_versions_active\"");
        sql.Should().Contain("WHERE \"status\" = 'active'");
    }
}
