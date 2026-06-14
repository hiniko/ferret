namespace Ferret.Abstractions.Search;

/// <summary>
/// SQL fragment emitted by an <see cref="ISearchBackend"/>. Each fragment selects two columns
/// (entity id, score) and is UNIONed with fragments from other backends in hybrid scoring.
/// </summary>
public readonly record struct SearchSqlFragment(
    string Sql,
    IReadOnlyList<KeyValuePair<string, object?>> Parameters)
{
}
