using System.Reflection;
using BenchmarkDotNet.Attributes;
using Ferret.Benchmarks;
using Ferret.Benchmarks.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests;

public class SearchJoinDepthBenchmarkConfigTests
{
    private static readonly Type BenchmarkType = typeof(SearchJoinDepthBenchmark);

    [Fact]
    public void Benchmark_exposes_depth_rowcount_and_cache_params()
    {
        var benchmarkMethod = BenchmarkType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .SingleOrDefault(m => m.GetCustomAttribute<BenchmarkAttribute>() is not null);

        benchmarkMethod.Should().NotBeNull("the depth-scaling benchmark must expose a [Benchmark] method");

        var depth = ParamsValues<int>("Depth");
        depth.Should().BeEquivalentTo([1, 2, 3, 4, 5]);

        var rowCount = ParamsValues<int>("RowCount");
        rowCount.Should().BeEquivalentTo([10, 100, 1_000, 10_000]);

        var cacheState = ParamsValues<CacheStateKind>("CacheState");
        cacheState.Should().Contain(CacheStateKind.Cold).And.Contain(CacheStateKind.Warm);
    }

    [Fact]
    public void Benchmark_has_global_setup_and_iteration_setup()
    {
        HasAttribute<GlobalSetupAttribute>().Should().BeTrue("global setup wires the harness + seeder");
        HasAttribute<IterationSetupAttribute>().Should().BeTrue("iteration setup resets cold caches");
    }

    private static IReadOnlyList<T> ParamsValues<T>(string memberName)
    {
        var member = BenchmarkType.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance)
            as MemberInfo
            ?? BenchmarkType.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);

        member.Should().NotBeNull($"the benchmark must declare a member named '{memberName}'");

        var attr = member!.GetCustomAttribute<ParamsAttribute>();
        attr.Should().NotBeNull($"member '{memberName}' must be decorated with [Params]");

        return attr!.Values.Cast<T>().ToList();
    }

    private static bool HasAttribute<TAttr>() where TAttr : Attribute =>
        BenchmarkType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Any(m => m.GetCustomAttribute<TAttr>() is not null);
}
