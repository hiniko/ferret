using Dapper;
using Npgsql;

namespace Ferret.Benchmarks.Infrastructure;

public sealed class DatasetSeedSpec
{
    public int Depth { get; init; } = 1;
    public int OwnerCount { get; init; } = 10;
    public int FanOut { get; init; } = 2;
    public int Seed { get; init; } = 1;
}

public sealed class DatasetSeedResult
{
    public required string MatchToken { get; init; }
    public required IReadOnlyCollection<Guid> ExpectedOwnerIds { get; init; }
    public required int Depth { get; init; }
    public required int OwnerCount { get; init; }
    public required int FanOut { get; init; }
}

public static class DatasetSeeder
{
    public const string MatchToken = "FERRETNEEDLE";

    private static readonly string[] HopTables =
    [
        "bench_hop1",
        "bench_hop2",
        "bench_hop3",
        "bench_hop4",
        "bench_hop5",
    ];

    public static async Task<DatasetSeedResult> SeedAsync(string connectionString, DatasetSeedSpec spec)
    {
        if (spec.Depth is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(spec), spec.Depth, "Depth must be between 1 and 5.");
        if (spec.OwnerCount < 1)
            throw new ArgumentOutOfRangeException(nameof(spec), spec.OwnerCount, "OwnerCount must be positive.");
        if (spec.FanOut < 1)
            throw new ArgumentOutOfRangeException(nameof(spec), spec.FanOut, "FanOut must be positive.");

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await Truncate(conn);

        var rng = new Random(spec.Seed);

        var expectedOwnerIds = new HashSet<Guid>();
        var ownerIds = new Guid[spec.OwnerCount];
        var ownerRows = new List<object>(spec.OwnerCount);
        for (var i = 0; i < spec.OwnerCount; i++)
        {
            var id = NextGuid(rng);
            ownerIds[i] = id;
            ownerRows.Add(new { Id = id, Name = $"owner-{i:D6}" });
        }

        await conn.ExecuteAsync(
            "INSERT INTO bench_owner (id, name) VALUES (@Id, @Name)",
            ownerRows);

        var planeMatches = Math.Max(1, ownerIds.Length / 10);

        var parents = ownerIds.Select(ownerId => (Id: ownerId, OwnerId: ownerId)).ToList();

        for (var hop = 1; hop <= spec.Depth; hop++)
        {
            var table = HopTables[hop - 1];
            var fkColumn = hop == 1 ? "owner_id" : "parent_id";
            var isLeaf = hop == spec.Depth;

            var matchOwners = isLeaf ? ownerIds.Take(planeMatches).ToHashSet() : [];
            if (isLeaf)
                expectedOwnerIds.UnionWith(matchOwners);

            var children = new List<(Guid Id, Guid OwnerId)>(parents.Count * spec.FanOut);
            var rows = new List<object>(parents.Count * spec.FanOut);

            foreach (var parent in parents)
            {
                for (var f = 0; f < spec.FanOut; f++)
                {
                    var id = NextGuid(rng);
                    children.Add((id, parent.OwnerId));
                    var label = $"hop{hop}-{rng.Next(0, 1_000_000):D7}";
                    if (isLeaf && matchOwners.Contains(parent.OwnerId))
                        label = $"{label} {MatchToken}";
                    rows.Add(new { Id = id, Fk = parent.Id, Label = label });
                }
            }

            await conn.ExecuteAsync(
                $"INSERT INTO {table} (id, {fkColumn}, label) VALUES (@Id, @Fk, @Label)",
                rows);

            parents = children;
        }

        return new DatasetSeedResult
        {
            MatchToken = MatchToken,
            ExpectedOwnerIds = expectedOwnerIds,
            Depth = spec.Depth,
            OwnerCount = spec.OwnerCount,
            FanOut = spec.FanOut,
        };
    }

    private static async Task Truncate(NpgsqlConnection conn)
    {
        await conn.ExecuteAsync(
            "TRUNCATE bench_hop5, bench_hop4, bench_hop3, bench_hop2, bench_hop1, bench_owner RESTART IDENTITY CASCADE");
    }

    private static Guid NextGuid(Random rng)
    {
        var bytes = new byte[16];
        rng.NextBytes(bytes);
        return new Guid(bytes);
    }
}
