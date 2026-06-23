using System.Reflection;
using Ferret.Core.Backends.FullText;
using Ferret.Core.Backends.Hybrid;
using Ferret.Core.Backends.Vector;

namespace Ferret.Core.Configuration;

public sealed class FerretOptions
{
    internal List<Assembly> ScannedAssemblies { get; } = [];
    internal Type NamingStrategyType { get; private set; } = typeof(SnakeCaseNamingStrategy);
    internal bool TrigramEnabled { get; private set; }
    internal TrigramOptions Trigram { get; } = new();
    internal bool FullTextEnabled { get; private set; }
    internal FullTextOptions FullText { get; } = new();
    internal bool VectorEnabled { get; private set; }
    internal VectorOptions Vector { get; } = new();
    internal bool HybridEnabled { get; private set; }
    internal HybridOptions Hybrid { get; } = new();
    internal int DefaultLimit { get; private set; } = 25;
    internal int MaxLimit { get; private set; } = 100;
    internal int SlowQueryThresholdMs { get; private set; } = 500;
    internal bool LogStatements { get; private set; }

    public FerretOptions WithPaginationDefaults(int defaultLimit, int maxLimit)
    {
        if (defaultLimit <= 0) throw new ArgumentOutOfRangeException(nameof(defaultLimit));
        if (maxLimit < defaultLimit) throw new ArgumentOutOfRangeException(nameof(maxLimit));
        DefaultLimit = defaultLimit;
        MaxLimit = maxLimit;
        return this;
    }

    /// <summary>
    /// Queries exceeding this duration are logged at <see cref="Microsoft.Extensions.Logging.LogLevel.Warning"/>.
    /// Default 500ms. Set to <c>0</c> or negative to disable.
    /// </summary>
    public FerretOptions WithSlowQueryThreshold(int thresholdMs)
    {
        SlowQueryThresholdMs = thresholdMs;
        return this;
    }

    /// <summary>
    /// When enabled, compiled SQL statements are attached to the <c>db.statement</c> activity tag
    /// and emitted at <see cref="Microsoft.Extensions.Logging.LogLevel.Debug"/>. Off by default — statements
    /// may contain identifiers and parameter shapes that should not appear in shared trace backends.
    /// </summary>
    public FerretOptions WithStatementLogging(bool enabled = true)
    {
        LogStatements = enabled;
        return this;
    }

    public FerretOptions ScanAssembly(Assembly asm)
    {
        if (!ScannedAssemblies.Contains(asm)) ScannedAssemblies.Add(asm);
        return this;
    }

    public FerretOptions UseNamingStrategy<T>() where T : INamingStrategy, new()
    {
        NamingStrategyType = typeof(T);
        return this;
    }

    public FerretOptions UseTrigramSearch(Action<TrigramOptions>? configure = null)
    {
        TrigramEnabled = true;
        configure?.Invoke(Trigram);
        return this;
    }

    public FerretOptions UseFullTextSearch(Action<FullTextOptions>? configure = null)
    {
        FullTextEnabled = true;
        configure?.Invoke(FullText);
        return this;
    }

    public FerretOptions UseVectorSearch(Action<VectorOptions>? configure = null)
    {
        VectorEnabled = true;
        configure?.Invoke(Vector);
        return this;
    }

    public FerretOptions UseHybridSearch(Action<HybridOptions>? configure = null)
    {
        HybridEnabled = true;
        configure?.Invoke(Hybrid);
        return this;
    }
}
