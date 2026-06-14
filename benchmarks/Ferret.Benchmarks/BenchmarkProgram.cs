using BenchmarkDotNet.Running;

namespace Ferret.Benchmarks;

public static class BenchmarkProgram
{
    public static Type[] GetBenchmarkTypes() =>
    [
        typeof(SearchJoinDepthBenchmark),
    ];

    public static int Run(string[] args)
    {
        var config = new BenchmarkConfig();
        BenchmarkSwitcher.FromTypes(GetBenchmarkTypes()).Run(args, config);
        return 0;
    }
}
