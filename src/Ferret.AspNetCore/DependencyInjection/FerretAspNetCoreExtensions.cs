using Ferret.AspNetCore.Binding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Ferret.AspNetCore.DependencyInjection;

public static class FerretAspNetCoreExtensions
{
    /// <summary>
    /// Registers <see cref="PaginationDefaultsResolver"/>. Enough for <c>MapFerret</c>
    /// endpoints, which parse the query string directly.
    /// </summary>
    /// <remarks>
    /// MVC controllers taking <c>[FromQuery] OffsetApiQuery</c> should also call
    /// <see cref="AddFerretMvcBinding"/> — see its remarks for why.
    /// </remarks>
    public static IServiceCollection AddFerretAspNetCore(this IServiceCollection services)
    {
        services.AddSingleton<PaginationDefaultsResolver>();
        return services;
    }

    /// <summary>
    /// Binds <see cref="OffsetApiQuery"/> / <see cref="CursorApiQuery"/> action parameters
    /// from the query string in one step, using the same parser as <c>MapFerret</c>.
    /// Call alongside <c>AddControllers()</c> when controllers accept these types.
    /// </summary>
    /// <remarks>
    /// Without this, MVC binds them as complex objects prefixed by the action parameter
    /// name: <c>Get([FromQuery] OffsetApiQuery q)</c> makes MVC look for <c>q.Q</c>,
    /// <c>q.limit</c>, … the moment a request carries <c>?q=</c>, so every property
    /// silently takes its default and searches quietly return unfiltered pages.
    /// </remarks>
    public static IServiceCollection AddFerretMvcBinding(this IServiceCollection services)
    {
        services.Configure<MvcOptions>(o =>
            o.ModelBinderProviders.Insert(0, new FerretQueryModelBinderProvider()));
        return services;
    }
}
