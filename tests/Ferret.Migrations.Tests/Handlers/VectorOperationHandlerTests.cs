using Ferret.Migrations.Handlers;
using Ferret.Migrations.Operations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Ferret.Migrations.Tests.Handlers;

public class VectorOperationHandlerTests
{
    [Fact]
    public void New_entity_emits_extension_sidecar_then_create_index()
    {
        var target = VectorModelFixtures.ModelWithGroup(dims: 8);
        var ops = new VectorMigrationOperationHandler()
            .GetOperations(source: null, target, Array.Empty<MigrationOperation>());

        ops.OfType<EnsurePgvectorExtensionOperation>().Should().ContainSingle();
        ops.OfType<EnsureVectorSidecarTableOperation>().Should().ContainSingle()
            .Which.SidecarTable.Should().Be("varticles_vec");
        ops.OfType<CreateVectorIndexOperation>().Should().ContainSingle()
            .Which.Group.Dimensions.Should().Be(8);

        ops.ToList().FindIndex(o => o is EnsurePgvectorExtensionOperation)
            .Should().BeLessThan(ops.ToList().FindIndex(o => o is EnsureVectorSidecarTableOperation));
    }

    [Fact]
    public void Removed_group_emits_drop()
    {
        var source = VectorModelFixtures.ModelWithGroup(dims: 8);
        var target = VectorModelFixtures.EmptyModel();
        var ops = new VectorMigrationOperationHandler().GetOperations(source, target, Array.Empty<MigrationOperation>());
        ops.OfType<DropVectorIndexOperation>().Should().ContainSingle();
    }

    [Fact]
    public void New_entity_emits_reindex_jobs_table()
    {
        var target = VectorModelFixtures.ModelWithGroup(dims: 8);
        var ops = new VectorMigrationOperationHandler()
            .GetOperations(source: null, target, Array.Empty<MigrationOperation>())
            .ToList();

        ops.OfType<EnsureReindexJobsTableOperation>().Should().ContainSingle();

        var jobsIdx = ops.FindIndex(o => o is EnsureReindexJobsTableOperation);
        var createIdx = ops.FindIndex(o => o is CreateVectorIndexOperation);
        jobsIdx.Should().BeLessThan(createIdx);
    }

    [Fact]
    public void New_entity_emits_version_registry_table()
    {
        var target = VectorModelFixtures.ModelWithGroup(dims: 8);
        var ops = new VectorMigrationOperationHandler()
            .GetOperations(source: null, target, Array.Empty<MigrationOperation>())
            .ToList();

        ops.OfType<EnsureVectorVersionRegistryOperation>().Should().ContainSingle();

        var registryIdx = ops.FindIndex(o => o is EnsureVectorVersionRegistryOperation);
        var createIdx = ops.FindIndex(o => o is CreateVectorIndexOperation);
        registryIdx.Should().BeLessThan(createIdx);
    }
}
