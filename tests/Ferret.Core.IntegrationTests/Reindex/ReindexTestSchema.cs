using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Search;
using Npgsql;

namespace Ferret.Core.IntegrationTests.Reindex;

internal static class ReindexTestSchema
{
    public const string SourceTable  = "reindex_docs";
    public const string SidecarTable = "reindex_docs_search";
    public const string IdColumn     = "id";
    public const string ColumnSuffix = "_tsv";

    public static FullTextGroup[] Groups() =>
    [
        new FullTextGroup
        {
            Name = "content",
            FullTextConfig = "english",
            Reindex = ReindexMode.Concurrent,
            Properties =
            [
                new FullTextGroupProperty { PropertyName = "Title", ColumnName = "title", Weight = FullTextWeightBucket.A },
                new FullTextGroupProperty { PropertyName = "Body",  ColumnName = "body",  Weight = FullTextWeightBucket.B },
            ],
        },
    ];

    public static async Task ResetAsync(NpgsqlConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            DROP TABLE IF EXISTS {SidecarTable} CASCADE;
            DROP TABLE IF EXISTS {SourceTable} CASCADE;
            CREATE TABLE {SourceTable} (
                id bigint PRIMARY KEY,
                title text NOT NULL,
                body  text NOT NULL
            );
            CREATE TABLE {SidecarTable} (
                id bigint PRIMARY KEY REFERENCES {SourceTable}(id) ON DELETE CASCADE,
                content_tsv tsvector,
                updated_at timestamptz NOT NULL DEFAULT now()
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task SeedAsync(NpgsqlConnection conn, int rows)
    {
        if (rows == 0) return;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {SourceTable} (id, title, body)
            SELECT g, 'title ' || g, 'body ' || g
            FROM generate_series(1, {rows}) AS g;
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
