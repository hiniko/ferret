using Ferret.Abstractions;
using Ferret.Abstractions.Attributes;
using Ferret.Core.Configuration;
using Ferret.Core.DependencyInjection;
using Ferret.Core.Embeddings;
using Ferret.Core.Engine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ferret.Core.Tests.Engine;

public class ResolveHybridTests
{
    [Fact]
    public void Multi_backend_entity_resolves_hybrid_when_not_forced()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(o => o.ScanAssembly(typeof(HybridFixtureEntity).Assembly).UsePostgres()
            .UseFullTextSearch().UseVectorSearch(v => v.UseEmbeddingProvider(_ => new FakeEmbeddingProvider(8)))
            .UseHybridSearch());
        using var sp = sc.BuildServiceProvider();
        var engine = (FerretEngine)sp.GetRequiredService<IFerretEngine>();
        var model = sp.GetRequiredService<EntityRegistry>().Get<HybridFixtureEntity>();

        engine.ResolveHybrid(model, forced: null).Should().NotBeNull();
        engine.ResolveHybrid(model, forced: SearchBackend.Vector).Should().BeNull();
    }
}

[SearchableEntity(Table = "hybfix")]
public sealed class HybridFixtureEntity : ISearchableEntity<Guid>
{
    public Guid Id { get; init; }
    [Searchable(Backend = SearchBackend.FullText, Group = "content")] public string Title { get; init; } = "";
    [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = 8)] public string Body { get; init; } = "";
}
