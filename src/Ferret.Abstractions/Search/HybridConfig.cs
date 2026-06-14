using Ferret.Abstractions.Attributes;

namespace Ferret.Abstractions.Search;

public sealed record HybridConfig
{
    public required IReadOnlyList<HybridBackendConfig> Backends { get; init; }
}

public sealed record HybridBackendConfig
{
    public required SearchBackend Backend { get; init; }
    public required double Weight { get; init; }
    /// <summary>NaN means "inherit the global HybridOptions default".</summary>
    public required double ConfidenceThreshold { get; init; }
}
