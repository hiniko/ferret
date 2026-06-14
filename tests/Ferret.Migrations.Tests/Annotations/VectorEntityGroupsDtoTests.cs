using System.Text.Json;
using Ferret.Abstractions.Search;
using Ferret.Migrations.Annotations;
using FluentAssertions;
using Xunit;

namespace Ferret.Migrations.Tests.Annotations;

public class VectorEntityGroupsDtoTests
{
    [Fact]
    public void Round_trips_through_json()
    {
        var dto = new VectorEntityGroupsDto
        {
            SidecarTable = "products_vec", SidecarSchema = null,
            SourceTable = "products", SourceSchema = null,
            IdColumn = "id", IdColumnType = "uuid", ColumnSuffix = "_embedding",
            HnswM = 16, HnswEfConstruction = 64,
            Groups =
            [
                new VectorGroupDto
                {
                    Name = "content", Dimensions = 8,
                    Properties = [new VectorGroupPropertyDto { PropertyName = "Body", ColumnName = "body", EmbeddingSource = "Body" }],
                }
            ],
        };

        var json = JsonSerializer.Serialize(dto);
        var back = JsonSerializer.Deserialize<VectorEntityGroupsDto>(json)!;
        back.Groups.Should().ContainSingle().Which.Dimensions.Should().Be(8);
        back.Groups[0].ToDomain().Properties[0].EmbeddingSource.Should().Be("Body");
    }
}
