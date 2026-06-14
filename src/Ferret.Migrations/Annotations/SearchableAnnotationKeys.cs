namespace Ferret.Migrations.Annotations;

/// <summary>
/// Versioned EF annotation keys used by the Ferret migrations bridge.
/// Bump the version suffix when the serialized payload format breaks.
/// </summary>
public static class SearchableAnnotationKeys
{
    /// <summary>Property-level: JSON-serialized <see cref="Ferret.Abstractions.Search.SearchIndexDefinition"/>.</summary>
    public const string SearchableIndexV1 = "Ferret:SearchableIndex:v1";

    /// <summary>Model-level: JSON string array of required Postgres extensions (e.g. <c>["pg_trgm"]</c>).</summary>
    public const string RequiredExtensionsV1 = "Ferret:RequiredExtensions:v1";

    /// <summary>Entity-level: JSON-serialized list of <c>FullTextEntityGroupsDto</c> (internal).</summary>
    public const string FullTextGroupsV1 = "Ferret:FullText:Groups:v1";

    /// <summary>Entity-level: JSON-serialized <c>VectorEntityGroupsDto</c> (internal).</summary>
    public const string VectorGroupsV1 = "Ferret:Vector:Groups:v1";
}
