using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Naming;
using Ferret.Abstractions.Search;
using Ferret.Abstractions.Sql;
using Ferret.Core.Backends.FullText;
using Ferret.Core.Backends.Trigram;
using Ferret.Core.Configuration;
using Ferret.Core.Engine;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ferret.Core.Tests.Engine;

public sealed class FerretEngineResolveBackendTests
{
    private sealed class TrigramOnly
    {
        public Guid Id { get; init; }
        [Searchable(Backend = SearchBackend.Trigram)] public string Name { get; init; } = "";
    }

    private sealed class FullTextOnly
    {
        public Guid Id { get; init; }
        [Searchable(Backend = SearchBackend.FullText, Group = "content")]
        public string Body { get; init; } = "";
    }

    private sealed class Hybrid
    {
        public Guid Id { get; init; }
        [Searchable(Backend = SearchBackend.Trigram)] public string Name { get; init; } = "";
        [Searchable(Backend = SearchBackend.FullText, Group = "content")] public string Body { get; init; } = "";
    }

    private sealed class NoSearchable
    {
        public Guid Id { get; init; }
    }

    private static EntityRegistry Registry(params Type[] types)
    {
        var naming = new SnakeCaseNamingStrategy();
        return EntityRegistry.Build(types, naming);
    }

    private static FerretEngine Engine(EntityRegistry registry, params ISearchBackend[] backends) =>
        new(registry, backends, NullLogger<FerretEngine>.Instance, new FerretRuntimeOptions());

    private static ISearchBackend Trigram() =>
        new TrigramSearchBackend(new PostgresDialect(), new TrigramOptions());

    private static ISearchBackend FullText(bool asPrimary = false) =>
        new FullTextSearchBackend(new PostgresDialect(), new FullTextOptions { AsPrimary = asPrimary });

    [Fact]
    public void Single_backend_entity_resolves_to_that_backend()
    {
        var reg = Registry(typeof(TrigramOnly), typeof(FullTextOnly));
        var trigram = Trigram();
        var fulltext = FullText();
        var engine = Engine(reg, trigram, fulltext);

        engine.ResolveBackend(reg.Get<TrigramOnly>()).Should().BeSameAs(trigram);
        engine.ResolveBackend(reg.Get<FullTextOnly>()).Should().BeSameAs(fulltext);
    }

    [Fact]
    public void Multi_backend_entity_without_AsPrimary_throws()
    {
        var reg = Registry(typeof(Hybrid));
        var engine = Engine(reg, Trigram(), FullText(asPrimary: false));

        Action act = () => engine.ResolveBackend(reg.Get<Hybrid>());
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AsPrimary*");
    }

    [Fact]
    public void Multi_backend_entity_routes_to_AsPrimary_backend()
    {
        var reg = Registry(typeof(Hybrid));
        var primaryFt = FullText(asPrimary: true);
        var engine = Engine(reg, Trigram(), primaryFt);

        engine.ResolveBackend(reg.Get<Hybrid>()).Should().BeSameAs(primaryFt);
    }

    [Fact]
    public void Entity_without_searchable_properties_throws()
    {
        var reg = Registry(typeof(NoSearchable));
        var engine = Engine(reg, Trigram());

        Action act = () => engine.ResolveBackend(reg.Get<NoSearchable>());
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no [Searchable] properties*");
    }

    [Fact]
    public void Single_backend_entity_with_unregistered_backend_throws()
    {
        var reg = Registry(typeof(FullTextOnly));
        var engine = Engine(reg, Trigram());          // fulltext NOT registered

        Action act = () => engine.ResolveBackend(reg.Get<FullTextOnly>());
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'fulltext' required by entity*not registered*");
    }
}
