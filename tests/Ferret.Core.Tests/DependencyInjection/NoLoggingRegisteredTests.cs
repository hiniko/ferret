using Ferret.Abstractions;
using Ferret.Abstractions.Attributes;
using Ferret.Core.DependencyInjection;
using Ferret.Core.Engine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ferret.Core.Tests.DependencyInjection;

/// <summary>
/// AddFerret must work in a container that never called AddLogging(). Requiring
/// ILogger&lt;FerretEngine&gt; made the container fail to build with an error naming an internal
/// type, which is a poor first experience for something Ferret only uses for diagnostics.
/// </summary>
public class NoLoggingRegisteredTests
{
    [SearchableEntity(Table = "widgets")]
    public sealed class Widget : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
    }

    private static ServiceCollection FerretOnly()
    {
        var services = new ServiceCollection();
        // Deliberately no AddLogging().
        services.AddFerret(o => o
            .ScanAssembly(typeof(Widget).Assembly)
            .UseTrigramSearch());
        return services;
    }

    [Fact]
    public void Engine_resolves_without_any_logging_registered()
    {
        using var sp = FerretOnly().BuildServiceProvider(validateScopes: true);

        sp.GetRequiredService<IFerretEngine>().Should().NotBeNull();
    }

    [Fact]
    public void Container_validates_without_any_logging_registered()
    {
        var act = () => FerretOnly().BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        act.Should().NotThrow(
            "ValidateOnBuild is how a startup surfaces missing dependencies; Ferret must not "
            + "be one of them just because the app has no logging");
    }

    [Fact]
    public void Logging_is_still_used_when_registered()
    {
        var services = FerretOnly();
        services.AddLogging();

        using var sp = services.BuildServiceProvider(validateScopes: true);

        sp.GetRequiredService<IFerretEngine>().Should().NotBeNull();
    }
}
