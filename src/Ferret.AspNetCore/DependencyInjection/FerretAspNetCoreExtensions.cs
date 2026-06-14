using Microsoft.Extensions.DependencyInjection;

namespace Ferret.AspNetCore.DependencyInjection;

public static class FerretAspNetCoreExtensions
{
    /// <summary>
    /// Marker hook so call-sites read clearly. Binders are wired via <c>[ModelBinder]</c>
    /// attributes on <see cref="OffsetApiQuery"/> and <see cref="CursorApiQuery"/>; no extra MVC config needed.
    /// </summary>
    public static IServiceCollection AddFerretAspNetCore(this IServiceCollection services)
    {
        services.AddSingleton<PaginationDefaultsResolver>();
        return services;
    }
}
