namespace Ferret.Core.Backends.Vector;

public static class VectorDdlBuilder
{
    public const string VersionRegistryTable = "ferret_vector_versions";

    public static string EnsureExtension() => "CREATE EXTENSION IF NOT EXISTS \"vector\";\n";

    public static string CreateVersionRegistry(string? schema) =>
        $"""
        CREATE TABLE IF NOT EXISTS {Qualify(schema, VersionRegistryTable)} (
            "version_id" bigserial PRIMARY KEY,
            "entity" text NOT NULL,
            "group_name" text NOT NULL,
            "model" text NOT NULL,
            "dimensions" integer NOT NULL,
            "column_name" text NOT NULL,
            "status" text NOT NULL,
            "created_at" timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT "uq_ferret_vector_versions" UNIQUE ("entity", "group_name", "version_id")
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "uq_ferret_vector_versions_active"
            ON {Qualify(schema, VersionRegistryTable)} ("entity", "group_name") WHERE "status" = 'active';
        """ + "\n";

    public static string CreateSidecarTable(
        string sidecarTable, string? sidecarSchema, string sourceTable, string? sourceSchema,
        string idColumn, string idColumnType) =>
        $"""
        CREATE TABLE IF NOT EXISTS {Qualify(sidecarSchema, sidecarTable)} (
            "{Escape(idColumn)}" {idColumnType} PRIMARY KEY REFERENCES {Qualify(sourceSchema, sourceTable)} ("{Escape(idColumn)}") ON DELETE CASCADE,
            "updated_at" timestamptz NOT NULL DEFAULT now()
        );
        """ + "\n";

    public static string AddGroupColumn(string sidecarTable, string? sidecarSchema, string column, int dimensions) =>
        $"ALTER TABLE {Qualify(sidecarSchema, sidecarTable)} ADD COLUMN IF NOT EXISTS \"{Escape(column)}\" vector({dimensions});\n";

    public static string CreateGroupIndex(string sidecarTable, string? sidecarSchema, string indexName, string column, int m, int efConstruction) =>
        $"CREATE INDEX IF NOT EXISTS \"{Escape(indexName)}\" ON {Qualify(sidecarSchema, sidecarTable)} USING hnsw (\"{Escape(column)}\" vector_cosine_ops) WITH (m = {m}, ef_construction = {efConstruction});\n";

    public static string DropGroupIndex(string? sidecarSchema, string indexName) =>
        sidecarSchema is null
            ? $"DROP INDEX IF EXISTS \"{Escape(indexName)}\";\n"
            : $"DROP INDEX IF EXISTS {Qualify(sidecarSchema, indexName)};\n";

    public static string DropGroupColumn(string sidecarTable, string? sidecarSchema, string column) =>
        $"ALTER TABLE {Qualify(sidecarSchema, sidecarTable)} DROP COLUMN IF EXISTS \"{Escape(column)}\";\n";

    private static string Qualify(string? schema, string table) =>
        schema is null ? $"\"{Escape(table)}\"" : $"\"{Escape(schema)}\".\"{Escape(table)}\"";

    private static string Escape(string identifier) => identifier.Replace("\"", "\"\"");
}
