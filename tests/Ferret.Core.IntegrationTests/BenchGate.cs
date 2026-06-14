using Xunit;

namespace Ferret.Core.IntegrationTests;

internal static class BenchGate
{
    public static void SkipUnlessEnabled() =>
        Skip.IfNot(
            Environment.GetEnvironmentVariable("FERRET_BENCH") == "1",
            "Benchmark-infrastructure test (spins a dedicated container + seeds large datasets). Set FERRET_BENCH=1 to run.");
}
