using System.Reflection;
using Ferret.Abstractions.Attributes;

namespace Ferret.Abstractions.Search;

public sealed record SearchablePropertyInfo
{
    public required PropertyInfo Property { get; init; }
    public required SearchBackend Backend { get; init; }
    public required float Weight { get; init; }
    public string? FullTextConfig { get; init; }
    public int EmbeddingDimensions { get; init; }
    public string? EmbeddingSource { get; init; }

    /// <summary>Resolved group name. Defaults to "default" when not set on the attribute.</summary>
    public string Group { get; init; } = "default";

    /// <summary>Rename hint copied from the attribute: the group this property previously belonged to.</summary>
    public string? PreviousGroup { get; init; }

    public JoinPath JoinPath { get; init; } = new();

    /// <summary>Resolved column name on the owning table (snake_case under default strategy).</summary>
    public required string ColumnName { get; init; }

    /// <summary>Resolved owning table name (root for direct, leaf hop for joined).</summary>
    public required string OwnerTableName { get; init; }
}
