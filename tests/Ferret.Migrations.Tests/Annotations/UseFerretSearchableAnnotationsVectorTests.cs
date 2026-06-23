using System.Text.Json;
using Ferret.Abstractions;
using Ferret.Abstractions.Attributes;
using Ferret.Migrations.Annotations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Ferret.Migrations.Tests.Annotations;

public class UseFerretSearchableAnnotationsVectorTests
{
    [SearchableEntity(Table = "vproducts")]
    public sealed class VProduct : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = 8)]
        public string Body { get; init; } = "";
    }

    private sealed class VProductContext : DbContext
    {
        public VProductContext(DbContextOptions<VProductContext> opts) : base(opts) { }
        public DbSet<VProduct> Products => Set<VProduct>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<VProduct>().ToTable("vproducts");
            modelBuilder.UseFerretSearchableAnnotations(typeof(VProduct).Assembly);
        }
    }

    private static IModel BuildModel()
    {
        var opts = new DbContextOptionsBuilder<VProductContext>()
            .UseNpgsql("Host=localhost;Database=ferret_test")
            .Options;
        using var ctx = new VProductContext(opts);
        return ctx.Model;
    }

    [Fact]
    public void Writes_VectorGroupsV1_annotation()
    {
        var entity = BuildModel().FindEntityType(typeof(VProduct))!;
        var ann = entity.FindAnnotation(SearchableAnnotationKeys.VectorGroupsV1);
        ann.Should().NotBeNull();
        var dto = JsonSerializer.Deserialize<VectorEntityGroupsDto>((string)ann!.Value!)!;
        dto.SidecarTable.Should().Be("vproducts_vec");
        dto.Groups.Should().ContainSingle().Which.Dimensions.Should().Be(8);
    }
}
