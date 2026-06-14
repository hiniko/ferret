namespace Ferret.Abstractions.Attributes;

/// <summary>
/// Per-entity hybrid fusion override for one backend. Multiple allowed (one per backend).
/// Weight multiplies the backend's RRF contribution (NaN = inherit the global HybridOptions
/// default); ConfidenceThreshold gates whether the backend contributes a given doc
/// (NaN = inherit the global HybridOptions default).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class HybridBackendAttribute : Attribute
{
    public SearchBackend Backend { get; }
    public double Weight { get; init; } = double.NaN;
    public double ConfidenceThreshold { get; init; } = double.NaN;
    public HybridBackendAttribute(SearchBackend backend) => Backend = backend;
}
