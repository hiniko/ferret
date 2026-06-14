using Ferret.Abstractions.Search;
using Npgsql;

namespace Ferret.Benchmarks.Infrastructure;

public static class CacheState
{
    public static async Task ForceColdAsync(NpgsqlConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DISCARD ALL;";
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task WarmAsync(NpgsqlConnection conn, SearchSqlFragment fragment, int runs = 3)
    {
        for (var i = 0; i < runs; i++)
            await ExecuteAsync(conn, fragment);
    }

    public static async Task<TimeSpan> MeasureColdMedianAsync(NpgsqlConnection conn, SearchSqlFragment fragment, int runs = 5)
    {
        var samples = new List<TimeSpan>(runs);
        for (var i = 0; i < runs; i++)
        {
            await ForceColdAsync(conn);
            samples.Add(await TimeOnceAsync(conn, fragment));
        }

        return Median(samples);
    }

    public static async Task<TimeSpan> MeasureWarmMedianAsync(NpgsqlConnection conn, SearchSqlFragment fragment, int runs = 5)
    {
        await WarmAsync(conn, fragment, runs);

        var samples = new List<TimeSpan>(runs);
        for (var i = 0; i < runs; i++)
            samples.Add(await TimeOnceAsync(conn, fragment));

        return Median(samples);
    }

    private static async Task<TimeSpan> TimeOnceAsync(NpgsqlConnection conn, SearchSqlFragment fragment)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        await ExecuteAsync(conn, fragment);
        return System.Diagnostics.Stopwatch.GetElapsedTime(start);
    }

    private static async Task ExecuteAsync(NpgsqlConnection conn, SearchSqlFragment fragment)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = fragment.Sql;
        foreach (var p in fragment.Parameters)
            cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
        }
    }

    private static TimeSpan Median(List<TimeSpan> samples)
    {
        samples.Sort();
        var mid = samples.Count / 2;
        return samples.Count % 2 == 1
            ? samples[mid]
            : TimeSpan.FromTicks((samples[mid - 1].Ticks + samples[mid].Ticks) / 2);
    }
}
