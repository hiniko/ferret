using Ferret.Core.Backends.Vector;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Backends.Vector;

public class VectorVersionRegistryTests
{
    private static VectorVersionRow Row(string model = "nomic-embed-text", int dims = 768) => new()
    {
        VersionId = 1, Entity = "docs", GroupName = "title",
        Model = model, Dimensions = dims, ColumnName = "title_embedding_v1", Status = "active",
    };

    [Fact]
    public void EnsureConfigDims_throws_when_provider_dims_differ_from_column_dims()
    {
        var act = () => VectorVersionRegistry.EnsureConfigDims(
            providerDims: 1536, columnDims: 768, entity: "docs", group: "title");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*provider dimensions (1536) do not match docs.title*768*");
    }

    [Fact]
    public void EnsureConfigDims_ok_when_equal()
    {
        var act = () => VectorVersionRegistry.EnsureConfigDims(768, 768, "docs", "title");
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureMatch_throws_when_no_active_row()
    {
        var act = () => VectorVersionRegistry.EnsureMatch(
            active: null, entity: "docs", group: "title", configuredModel: "nomic-embed-text", configuredDims: 768);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no active embedding version for docs.title*reindex*");
    }

    [Fact]
    public void EnsureMatch_throws_on_dimension_drift()
    {
        var act = () => VectorVersionRegistry.EnsureMatch(
            Row(dims: 768), "docs", "title", "nomic-embed-text", configuredDims: 1536);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*embedding dimensions changed*768*1536*reindex required*");
    }

    [Fact]
    public void EnsureMatch_throws_on_model_drift()
    {
        var act = () => VectorVersionRegistry.EnsureMatch(
            Row(model: "nomic-embed-text"), "docs", "title", "text-embedding-3-small", 768);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*embedding model changed*nomic-embed-text*text-embedding-3-small*reindex required*");
    }

    [Fact]
    public void EnsureMatch_ok_when_model_and_dims_match()
    {
        var act = () => VectorVersionRegistry.EnsureMatch(
            Row(), "docs", "title", "nomic-embed-text", 768);
        act.Should().NotThrow();
    }
}
