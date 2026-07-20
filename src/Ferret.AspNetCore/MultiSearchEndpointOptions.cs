using System.Linq.Expressions;
using System.Reflection;
using Ferret.Abstractions.Models;

namespace Ferret.AspNetCore;

/// <summary>Map-time configuration for a multisearch endpoint: the entity projections and per-entity limits.</summary>
public sealed class MultiSearchEndpointOptions
{
    internal sealed record HitField(string MemberName, Func<object, object?> Getter);

    internal List<MultiSearchEntityDefinition> Definitions { get; } = [];

    /// <summary>Per entity CLR type, the projected members to emit on the wire (raw member name + getter;
    /// the endpoint applies the app's JSON naming policy at request time). Only these appear in the
    /// response — unprojected entity properties are never serialized.</summary>
    internal Dictionary<Type, IReadOnlyList<HitField>> HitShapes { get; } = [];

    internal Func<MultiSearchResult, object>? Shaper { get; private set; }

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

    public MultiSearchEndpointOptions Entity<T>(
        Expression<Func<T, T>> projection,
        Action<MultiSearchEntityOptions>? configure = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (projection.Body is not MemberInitExpression init)
        {
            throw new InvalidOperationException(
                $"MapFerretMultiSearch projections must be member-inits (u => new {typeof(T).Name} {{ … }}) so response field names are known.");
        }

        var options = new MultiSearchEntityOptions();
        configure?.Invoke(options);
        Definitions.Add(new MultiSearchEntityDefinition
        {
            EntityType = typeof(T),
            Projection = projection,
            Filters = options.Filters,
        });
        HitShapes[typeof(T)] = init.Bindings
            .Select(b => new HitField(b.Member.Name, CompileGetter<T>(b.Member)))
            .ToList();
        return this;
    }

    /// <summary>Replaces the default <see cref="MultiSearchApiResponse"/> wire shape: receives the raw
    /// engine result (hits are partially-populated entities) and returns the object to serialize.</summary>
    public MultiSearchEndpointOptions ShapeResponse(Func<MultiSearchResult, object> shaper)
    {
        ArgumentNullException.ThrowIfNull(shaper);
        Shaper = shaper;
        return this;
    }

    private static Func<object, object?> CompileGetter<T>(MemberInfo member)
    {
        var obj = Expression.Parameter(typeof(object), "o");
        var access = Expression.MakeMemberAccess(Expression.Convert(obj, typeof(T)), member);
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(access, typeof(object)), obj).Compile();
    }
}
