using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Ferret.Hosting.DependencyInjection;

public static class FerretHostingServiceCollectionExtensions
{
    public static IServiceCollection AddFerretReindexHostedService(
        this IServiceCollection services,
        Action<ReindexHostedServiceOptions>? configure = null)
    {
        if (services.All(d => d.ServiceType != typeof(IReindexRunner)))
        {
            throw new InvalidOperationException(
                "AddFerretReindexHostedService requires an IReindexRunner in the container. " +
                "Call AddFerret(...).UseFullTextSearch(...) before AddFerretReindexHostedService so the reindex runner is registered.");
        }

        var options = services.AddOptions<ReindexHostedServiceOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ReindexHostedService>());

        return services;
    }
}
