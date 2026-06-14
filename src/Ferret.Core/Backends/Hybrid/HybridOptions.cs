namespace Ferret.Core.Backends.Hybrid;

public sealed class HybridOptions
{
    public int RrfK { get; set; } = 60;
    public int CandidateDepth { get; set; } = 5;
    public double DefaultWeight { get; set; } = 1.0;
    public double? DefaultConfidenceThreshold { get; set; }
    public int MaxSearchCursorOffset { get; set; } = 200;
}
