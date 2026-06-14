namespace Ferret.Core.Backends.Hybrid;

internal sealed record RankedCandidateRequest
{
    public required string SourceTable { get; init; }
    public string? SidecarSchema { get; init; }
    public required IReadOnlyList<string> KeyColumns { get; init; }
    public required string SearchTerm { get; init; }
    public required int Depth { get; init; }
    public double? ConfidenceThreshold { get; init; }
    public required string CteName { get; init; }
    public string? QueryVectorParameterName { get; init; }
}
