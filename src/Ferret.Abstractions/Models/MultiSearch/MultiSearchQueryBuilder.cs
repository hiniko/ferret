using System.Linq.Expressions;

namespace Ferret.Abstractions.Models;

/// <summary>Fluent builder for <see cref="MultiSearchQuery"/>.</summary>
public sealed class MultiSearchQueryBuilder
{
    private readonly List<MultiSearchEntityDefinition> _entities = [];
    private string _search = "";
    private int _perEntityLimit = 5;
    private IReadOnlyList<string>? _types;

    public MultiSearchQueryBuilder Search(string term)
    {
        _search = term;
        return this;
    }

    public MultiSearchQueryBuilder PerEntityLimit(int limit)
    {
        _perEntityLimit = limit;
        return this;
    }

    public MultiSearchQueryBuilder Types(params string[] types)
    {
        _types = types;
        return this;
    }

    public MultiSearchQueryBuilder Entity<T, THit>(
        Expression<Func<T, THit>> projection,
        Action<MultiSearchEntityOptions>? configure = null)
        where T : class
        where THit : MultiSearchHit
    {
        ArgumentNullException.ThrowIfNull(projection);
        var options = new MultiSearchEntityOptions();
        configure?.Invoke(options);
        _entities.Add(new MultiSearchEntityDefinition
        {
            EntityType = typeof(T),
            HitType = typeof(THit),
            Projection = projection,
            Filters = options.Filters,
        });
        return this;
    }

    public MultiSearchQuery Build()
    {
        if (string.IsNullOrWhiteSpace(_search))
        {
            throw new InvalidOperationException("Multisearch requires a non-blank search term; there is no browse mode.");
        }
        if (_entities.Count == 0)
        {
            throw new InvalidOperationException("Multisearch requires at least one entity registration.");
        }
        var duplicate = _entities.GroupBy(e => e.EntityType).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Entity {duplicate.Key.Name} is registered more than once.");
        }
        if (_perEntityLimit < 1)
        {
            throw new ArgumentException($"PerEntityLimit must be positive; got {_perEntityLimit}.");
        }

        return new MultiSearchQuery
        {
            Search = _search,
            PerEntityLimit = _perEntityLimit,
            Entities = _entities,
            Types = _types,
        };
    }
}
