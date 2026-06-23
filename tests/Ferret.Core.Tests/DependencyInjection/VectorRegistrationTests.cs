using Ferret.Abstractions.Embeddings;
using Ferret.Abstractions.Search;
using Ferret.Core.Configuration;
using Ferret.Core.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ferret.Core.Tests.DependencyInjection;

public class VectorRegistrationTests
{
    [Fact]
    public void Provider_with_ctor_deps_resolves_from_built_provider()
    {
        var sc = new ServiceCollection();
        sc.AddSingleton(new DepThing());
        sc.AddFerret(o => o
            .ScanAssembly(typeof(VectorRegistrationTests).Assembly)
            .UseVectorSearch(v =>
                v.UseEmbeddingProvider(sp => new ProviderWithDeps(sp.GetRequiredService<DepThing>(), dimensions: 8))));

        using var sp = sc.BuildServiceProvider();

        sp.GetServices<ISearchBackend>().Should().Contain(b => b.Name == "vector");
        sp.GetRequiredService<IEmbeddingProvider>().Dimensions.Should().Be(8);
    }

    private sealed class DepThing { }
    private sealed class ProviderWithDeps : IEmbeddingProvider
    {
        public ProviderWithDeps(DepThing dep, int dimensions) { _ = dep; Dimensions = dimensions; }
        public int Dimensions { get; }
        public string ModelId => "test";
        public Task<float[]> EmbedAsync(string text, CancellationToken ct) => Task.FromResult(new float[Dimensions]);
    }
}
