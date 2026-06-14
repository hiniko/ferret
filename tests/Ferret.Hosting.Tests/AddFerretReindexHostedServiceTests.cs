using System.Data.Common;
using Ferret.Abstractions.Session;
using Ferret.Abstractions.Sql;
using Ferret.Core.Engine.Reindex;
using Ferret.Hosting;
using Ferret.Hosting.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ferret.Hosting.Tests;

public class AddFerretReindexHostedServiceTests
{
    private sealed class FakeRunner : IReindexRunner
    {
        public Task<int> DrainAsync(IFerretSession session, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> DrainAsync(IFerretSession session, ReindexDrainOptions options, CancellationToken ct = default) => Task.FromResult(0);
    }

    [Fact]
    public void AddFerretReindexHostedService_registers_hosted_service()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IReindexRunner>(new FakeRunner());

        services.AddFerretReindexHostedService(o =>
            o.SessionFactory = (_, _) => Task.FromResult<IFerretSession>(null!));

        using var sp = services.BuildServiceProvider();

        var hosted = sp.GetServices<IHostedService>();
        hosted.Should().ContainSingle(h => h is ReindexHostedService);
    }

    [Fact]
    public void AddFerretReindexHostedService_throws_when_AddFerret_missing()
    {
        var services = new ServiceCollection();

        var act = () => services.AddFerretReindexHostedService(o =>
            o.SessionFactory = (_, _) => Task.FromResult<IFerretSession>(null!));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AddFerret*");
    }
}
