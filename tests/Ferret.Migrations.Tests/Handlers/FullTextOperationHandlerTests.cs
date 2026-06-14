using Ferret.Abstractions.Attributes;
using Ferret.Migrations.Annotations;
using Ferret.Migrations.Handlers;
using Ferret.Migrations.Operations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Ferret.Migrations.Tests.Handlers;

public class FullTextOperationHandlerTests
{
    private static SearchableMigrationOperationHandler Handler() =>
        new SearchableMigrationOperationHandler();

    [Fact]
    public void New_entity_emits_EnsureSidecarTable_then_CreateGroup_per_group()
    {
        var source = ModelFixtures.EmptyModel();
        var target = ModelFixtures.ModelWithGroups(ModelFixtures.OneGroup());

        var ops = Handler().GetOperations(source, target, Array.Empty<MigrationOperation>());

        ops.Should().HaveCount(2);
        ops[0].Should().BeOfType<EnsureSidecarTableOperation>()
            .Which.SidecarTable.Should().Be("articles_search");
        var create = ops[1].Should().BeOfType<CreateFullTextGroupOperation>().Subject;
        create.Group.Name.Should().Be("default");
        create.SidecarTable.Should().Be("articles_search");
        create.SourceTable.Should().Be("articles");
        create.ColumnSuffix.Should().Be("_tsv");
        create.ReindexMode.Should().Be(ReindexMode.Inline);
    }

    [Fact]
    public void Removed_entity_emits_DropGroup_per_group()
    {
        var source = ModelFixtures.ModelWithGroups(ModelFixtures.OneGroup());
        var target = ModelFixtures.EmptyModel();

        var ops = Handler().GetOperations(source, target, Array.Empty<MigrationOperation>());

        ops.Should().HaveCount(1);
        var drop = ops[0].Should().BeOfType<DropFullTextGroupOperation>().Subject;
        drop.GroupName.Should().Be("default");
        drop.SidecarTable.Should().Be("articles_search");
        drop.ColumnSuffix.Should().Be("_tsv");
    }

    [Fact]
    public void Changed_group_emits_AlterFullTextGroupOperation()
    {
        var source = ModelFixtures.ModelWithGroups(ModelFixtures.OneGroup(fullTextConfig: "english"));
        var changed = ModelFixtures.OneGroup(fullTextConfig: "simple");
        var target = ModelFixtures.ModelWithGroups(changed);

        var ops = Handler().GetOperations(source, target, Array.Empty<MigrationOperation>());

        ops.Should().HaveCount(1);
        var alter = ops[0].Should().BeOfType<AlterFullTextGroupOperation>().Subject;
        alter.Group.Name.Should().Be("default");
        alter.Group.FullTextConfig.Should().Be("simple");
    }

    [Fact]
    public void Adding_property_with_new_joined_table_emits_CreateJoinedTableTriggerOperation()
    {
        var source = ModelFixtures.ModelWithGroups(ModelFixtures.OneGroup());
        var target = ModelFixtures.ModelWithGroups(ModelFixtures.GroupWithJoinedTables("comments"));

        var ops = Handler().GetOperations(source, target, Array.Empty<MigrationOperation>());

        var create = ops.OfType<CreateJoinedTableTriggerOperation>().Should().ContainSingle().Subject;
        create.JoinedTable.Should().Be("comments");
        create.SourceTable.Should().Be("articles");
        create.SidecarTable.Should().Be("articles_search");
        create.IdColumn.Should().Be("id");
        ops.OfType<DropJoinedTableTriggerOperation>().Should().BeEmpty();
    }

    [Fact]
    public void Removing_last_property_referencing_joined_table_emits_DropJoinedTableTriggerOperation()
    {
        var source = ModelFixtures.ModelWithGroups(ModelFixtures.GroupWithJoinedTables("comments"));
        var target = ModelFixtures.ModelWithGroups(ModelFixtures.OneGroup());

        var ops = Handler().GetOperations(source, target, Array.Empty<MigrationOperation>());

        var drop = ops.OfType<DropJoinedTableTriggerOperation>().Should().ContainSingle().Subject;
        drop.JoinedTable.Should().Be("comments");
        drop.SourceTable.Should().Be("articles");
        drop.SidecarTable.Should().Be("articles_search");
        ops.OfType<CreateJoinedTableTriggerOperation>().Should().BeEmpty();
    }

    [Fact]
    public void Owner_local_only_change_emits_no_joined_table_trigger_ops()
    {
        var source = ModelFixtures.ModelWithGroups(ModelFixtures.OneGroup(fullTextConfig: "english"));
        var target = ModelFixtures.ModelWithGroups(ModelFixtures.OneGroup(fullTextConfig: "simple"));

        var ops = Handler().GetOperations(source, target, Array.Empty<MigrationOperation>());

        ops.OfType<CreateJoinedTableTriggerOperation>().Should().BeEmpty();
        ops.OfType<DropJoinedTableTriggerOperation>().Should().BeEmpty();
    }

    [Fact]
    public void Inline_joined_group_prepends_EnsureReindexJobsTableOperation()
    {
        // A default-Inline cross-entity group still emits a joined-table trigger
        // whose body INSERTs into ferret_reindex_jobs. The jobs table must be
        // ensured even though no group uses a non-Inline reindex mode.
        var source = ModelFixtures.EmptyModel();
        var target = ModelFixtures.ModelWithGroups(ModelFixtures.GroupWithJoinedTables("comments"));

        var ops = Handler().GetOperations(source, target, Array.Empty<MigrationOperation>());

        ops.OfType<CreateJoinedTableTriggerOperation>().Should().ContainSingle()
            .Which.JoinedTable.Should().Be("comments");
        ops.Should().ContainSingle(o => o is EnsureReindexJobsTableOperation);
        var jobsIndex = ops.ToList().FindIndex(o => o is EnsureReindexJobsTableOperation);
        var triggerIndex = ops.ToList().FindIndex(o => o is CreateJoinedTableTriggerOperation);
        jobsIndex.Should().BeLessThan(triggerIndex);
    }

    [Fact]
    public void NonInline_reindex_mode_prepends_EnsureReindexJobsTableOperation()
    {
        var source = ModelFixtures.EmptyModel();
        var target = ModelFixtures.ModelWithGroups(ModelFixtures.OneGroup(reindex: ReindexMode.Concurrent));

        var ops = Handler().GetOperations(source, target, Array.Empty<MigrationOperation>());

        ops.Should().HaveCount(3);
        ops[0].Should().BeOfType<EnsureReindexJobsTableOperation>();
        ops[1].Should().BeOfType<EnsureSidecarTableOperation>();
        var create = ops[2].Should().BeOfType<CreateFullTextGroupOperation>().Subject;
        create.ReindexMode.Should().Be(ReindexMode.Concurrent);
    }
}
