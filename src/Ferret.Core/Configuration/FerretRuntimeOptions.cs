namespace Ferret.Core.Configuration;

/// <summary>Resolved runtime knobs consumed by <c>FerretEngine</c>. Populated from <see cref="FerretOptions"/> at DI time.</summary>
public sealed class FerretRuntimeOptions
{
    public int SlowQueryThresholdMs { get; init; } = 500;
    public bool LogStatements { get; init; }
}
