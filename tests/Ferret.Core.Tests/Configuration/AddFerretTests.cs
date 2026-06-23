using Ferret.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Ferret.Core.Tests.Configuration;

public class AddFerretTests
{
    [SearchableEntity]
    public sealed class Product : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
    }

    [Fact]
    public void AddFerret_registers_engine_and_dialect_and_registry()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts => opts
            .ScanAssembly(typeof(Product).Assembly));
        var sp = sc.BuildServiceProvider();

        sp.GetRequiredService<IFerretEngine>().Should().NotBeNull();
        sp.GetRequiredService<ISqlDialect>().Should().BeOfType<PostgresDialect>();
        sp.GetRequiredService<EntityRegistry>().Get<Product>().Should().NotBeNull();
    }
}
