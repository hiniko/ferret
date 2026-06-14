using Microsoft.Extensions.DependencyInjection;

namespace Ferret.Core.Configuration;

public sealed class FerretBuilder
{
    public IServiceCollection Services { get; }
    public FerretOptions Options { get; } = new();

    public FerretBuilder(IServiceCollection services) => Services = services;
}
