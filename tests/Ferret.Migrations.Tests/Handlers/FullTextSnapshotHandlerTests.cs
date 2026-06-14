using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ferret.Migrations.Tests.Handlers;

public class FullTextSnapshotHandlerTests
{
    [Fact]
    public void Emits_entity_HasAnnotation_for_FullTextGroupsV1()
    {
        var dto = ModelFixtures.OneGroup();
        var model = ModelFixtures.ModelWithGroups(dto).Model;
        var handler = new SearchableSnapshotHandler();
        var builder = new IndentedStringBuilder();

        handler.GenerateSnapshot(model, builder);

        var output = builder.ToString();
        output.Should().Contain("modelBuilder.Entity(\"");
        output.Should().Contain($"\"{SearchableAnnotationKeys.FullTextGroupsV1}\"");
        output.Should().Contain("articles_search");
    }

    [Fact]
    public void Skips_entity_annotation_when_not_present()
    {
        var model = ModelFixtures.EmptyModel().Model;
        var handler = new SearchableSnapshotHandler();
        var builder = new IndentedStringBuilder();

        handler.GenerateSnapshot(model, builder);

        var output = builder.ToString();
        output.Should().NotContain(SearchableAnnotationKeys.FullTextGroupsV1);
    }

    [Fact]
    public void JoinPath_round_trips_through_serialize_deserialize_cycle()
    {
        var domain = new FullTextGroup
        {
            Name = "content",
            FullTextConfig = "english",
            Reindex = ReindexMode.Inline,
            Properties =
            [
                new FullTextGroupProperty
                {
                    PropertyName = "AuthorName",
                    ColumnName = "name",
                    Weight = FullTextWeightBucket.B,
                    FullTextConfigOverride = null,
                    Join = new JoinPath
                    {
                        Hops =
                        [
                            new JoinHop
                            {
                                TableName = "authors",
                                TableAlias = "a0",
                                ForeignKeyColumn = "author_id",
                                EntityType = typeof(ModelFixtures.Article),
                                Cardinality = JoinCardinality.ManyToOne,
                                ForeignKeyOwningSide = true,
                                Schema = "public",
                            },
                        ],
                    },
                },
            ],
        };

        var json = JsonSerializer.Serialize(FullTextGroupDto.FromDomain(domain));
        var roundTripped = JsonSerializer.Deserialize<FullTextGroupDto>(json)!.ToDomain();

        roundTripped.Should().BeEquivalentTo(domain, opts => opts.Excluding(p =>
            p.Path.EndsWith("EntityType")));

        var prop = roundTripped.Properties.Single();
        prop.Join.Should().NotBeNull();
        var hop = prop.Join!.Hops.Single();
        hop.TableName.Should().Be("authors");
        hop.Schema.Should().Be("public");
        hop.ForeignKeyColumn.Should().Be("author_id");
        hop.Cardinality.Should().Be(JoinCardinality.ManyToOne);
        hop.ForeignKeyOwningSide.Should().BeTrue();
    }

    [Fact]
    public void Owner_local_group_serializes_without_join_payload()
    {
        var dto = ModelFixtures.OneGroup();

        var json = JsonSerializer.Serialize(dto);

        json.Should().NotContain("\"Join\":{");
        var roundTripped = JsonSerializer.Deserialize<FullTextEntityGroupsDto>(json)!;
        roundTripped.Groups.Single().Properties.Single().ToDomain().Join.Should().BeNull();
    }
}
