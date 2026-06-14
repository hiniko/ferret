using Ferret.Core.Backends.Hybrid;
using Ferret.Core.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ferret.Core.Tests.DependencyInjection;

public class HybridRegistrationTests
{
    [Fact]
    public void UseHybridSearch_registers_options_singleton()
    {
        var sc = new ServiceCollection();
        sc.AddFerret(o => o.ScanAssembly(typeof(HybridRegistrationTests).Assembly).UsePostgres()
            .UseHybridSearch(h => h.RrfK = 42));
        using var sp = sc.BuildServiceProvider();
        sp.GetRequiredService<HybridOptions>().RrfK.Should().Be(42);
    }
}
