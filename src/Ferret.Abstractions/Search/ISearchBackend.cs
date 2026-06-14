using Ferret.Abstractions.Attributes;

namespace Ferret.Abstractions.Search;

public interface ISearchBackend
{
    /// <summary>Stable identifier — <c>"trigram"</c>, <c>"fulltext"</c>, <c>"vector"</c>.</summary>
    string Name { get; }

    /// <summary>Returns <c>true</c> when this backend handles the given property's <see cref="SearchableAttribute.Backend"/>.</summary>
    bool CanHandle(SearchablePropertyInfo property);

    /// <summary>Emit the (id, score) ranking SQL for the supplied properties.</summary>
    SearchSqlFragment BuildRankingQuery(SearchContext context);

    /// <summary>Index DDL for a single property; <c>null</c> if the backend has no index.</summary>
    SearchIndexDefinition? GetIndexDefinition(SearchablePropertyInfo property);

    /// <summary>Vector backend: resolve a text query to an embedding. Returns <c>null</c> for non-vector backends.</summary>
    Task<float[]?> ResolveQueryVectorAsync(string searchTerm, CancellationToken ct);
}
