using System.Data.Common;
using BenchmarkDotNet.Attributes;
using Ferret.Abstractions;
using Ferret.Abstractions.Models;
using Ferret.Benchmarks.Infrastructure;
using Ferret.Benchmarks.Model;
using Ferret.Core.DependencyInjection;
using Ferret.Core.Engine;
using Ferret.Core.Sql;
using Ferret.Hydration.Dapper;
using Ferret.Hydration.Dapper.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ferret.Benchmarks;

[MemoryDiagnoser]
public class SearchJoinDepthBenchmark
{
    private BenchPostgresHarness _harness = null!;
    private IFerretEngine _engine = null!;
    private string _matchToken = DatasetSeeder.MatchToken;

    internal static string? CurrentConnectionString { get; private set; }
    internal static string CurrentMatchToken { get; private set; } = DatasetSeeder.MatchToken;

    [Params(1, 2, 3, 4, 5)]
    public int Depth { get; set; }

    [Params(10, 100, 1_000, 10_000)]
    public int RowCount { get; set; }

    [Params(CacheStateKind.Cold, CacheStateKind.Warm)]
    public CacheStateKind CacheState { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _harness = new BenchPostgresHarness();
        await _harness.StartAsync();

        var result = await DatasetSeeder.SeedAsync(_harness.ConnectionString, new DatasetSeedSpec
        {
            Depth = Depth,
            OwnerCount = RowCount,
            FanOut = 2,
            Seed = 1,
        });
        _matchToken = result.MatchToken;
        CurrentConnectionString = _harness.ConnectionString;
        CurrentMatchToken = _matchToken;

        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts => opts
            .ScanAssembly(typeof(HopGraph).Assembly)
            .UseTrigramSearch()
            .UseDapperHydration());
        _engine = sc.BuildServiceProvider().GetRequiredService<IFerretEngine>();

        if (CacheState == CacheStateKind.Warm)
            await RunSearchAsync();
    }

    [GlobalCleanup]
    public async Task GlobalCleanup() => await _harness.DisposeAsync();

    [IterationSetup]
    public void IterationSetup()
    {
        if (CacheState != CacheStateKind.Cold)
            return;

        using var conn = new NpgsqlConnection(_harness.ConnectionString);
        conn.Open();
        Infrastructure.CacheState.ForceColdAsync(conn).GetAwaiter().GetResult();
    }

    [Benchmark]
    public async Task<int> SearchOffset() => await RunSearchAsync();

    private async Task<int> RunSearchAsync() => Depth switch
    {
        1 => await SearchAsync<HopGraph.Owner1>(),
        2 => await SearchAsync<HopGraph.Owner2>(),
        3 => await SearchAsync<HopGraph.Owner3>(),
        4 => await SearchAsync<HopGraph.Owner4>(),
        5 => await SearchAsync<HopGraph.Owner5>(),
        _ => throw new ArgumentOutOfRangeException(nameof(Depth)),
    };

    private async Task<int> SearchAsync<T>() where T : class
    {
        var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_harness.ConnectionString)),
            new PostgresDialect());

        var result = await _engine.SearchOffsetAsync<T, Guid>(session, new PagedQuery<T, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = _matchToken,
            Limit = 25,
            Page = 0,
        });

        return result.Items.Count;
    }
}
