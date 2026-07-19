using System.Linq.Expressions;
using Ferret.Abstractions.Models;

namespace Ferret.AspNetCore;

/// <summary>Map-time configuration for a multisearch endpoint: the entity/hit shapes and per-entity limits.</summary>
public sealed class MultiSearchEndpointOptions
{
    internal List<MultiSearchEntityDefinition> Definitions { get; } = [];

    internal int DefaultPerEntityLimit { get; private set; } = 5;

    internal int MaxPerEntityLimit { get; private set; } = 25;

    public string? Name { get; set; }

    public string? Tag { get; set; }

    public string? Summary { get; set; }

    public MultiSearchEndpointOptions WithLimits(int defaultPerEntityLimit, int maxPerEntityLimit)
    {
        DefaultPerEntityLimit = defaultPerEntityLimit;
        MaxPerEntityLimit = maxPerEntityLimit;
        return this;
    }

    public MultiSearchEndpointOptions Entity<T, THit>(
        Expression<Func<T, THit>> projection,
        Action<MultiSearchEntityOptions>? configure = null)
        where T : class
        where THit : MultiSearchHit
    {
        ArgumentNullException.ThrowIfNull(projection);
        var options = new MultiSearchEntityOptions();
        configure?.Invoke(options);
        Definitions.Add(new MultiSearchEntityDefinition
        {
            EntityType = typeof(T),
            HitType = typeof(THit),
            Projection = projection,
            Filters = options.Filters,
        });
        return this;
    }
}
