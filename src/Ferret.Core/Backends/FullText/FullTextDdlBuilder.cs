using System.Text;
using Ferret.Abstractions.Search;

namespace Ferret.Core.Backends.FullText;

public static class FullTextDdlBuilder
{
    public static string CreateSidecarTable(
        string sidecarTable,
        string? sidecarSchema,
        string sourceTable,
        string? sourceSchema,
        string idColumn,
        string idColumnType) =>
        CreateSidecarTable(sidecarTable, sidecarSchema, sourceTable, sourceSchema,
            new[] { (idColumn, idColumnType) });

    public static string CreateSidecarTable(
        string sidecarTable,
        string? sidecarSchema,
        string sourceTable,
        string? sourceSchema,
        IReadOnlyList<(string Column, string Type)> keyParts)
    {
        var sidecar = Qualify(sidecarSchema, sidecarTable);
        var source  = Qualify(sourceSchema,  sourceTable);
        var sb = new StringBuilder();
        sb.Append("CREATE TABLE IF NOT EXISTS ").Append(sidecar).AppendLine(" (");

        if (keyParts.Count == 1)
        {
            var (col, type) = keyParts[0];
            sb.Append("    \"").Append(Escape(col)).Append("\" ").Append(type)
              .Append(" PRIMARY KEY REFERENCES ").Append(source)
              .Append(" (\"").Append(Escape(col)).AppendLine("\") ON DELETE CASCADE,");
        }
        else
        {
            foreach (var (col, type) in keyParts)
                sb.Append("    \"").Append(Escape(col)).Append("\" ").Append(type).AppendLine(" NOT NULL,");

            var cols = string.Join(", ", keyParts.Select(k => $"\"{Escape(k.Column)}\""));
            sb.Append("    PRIMARY KEY (").Append(cols).AppendLine("),");
            sb.Append("    FOREIGN KEY (").Append(cols).Append(") REFERENCES ").Append(source)
              .Append(" (").Append(cols).AppendLine(") ON DELETE CASCADE,");
        }

        sb.AppendLine("    \"updated_at\" timestamptz NOT NULL DEFAULT now()");
        sb.AppendLine(");");
        return sb.ToString();
    }

    public static string AddGroupColumn(string sidecarTable, string? sidecarSchema, string groupColumn) =>
        $"ALTER TABLE {Qualify(sidecarSchema, sidecarTable)} ADD COLUMN IF NOT EXISTS \"{Escape(groupColumn)}\" tsvector;\n";

    public static string CreateGroupIndex(string sidecarTable, string? sidecarSchema, string indexName, string groupColumn) =>
        $"CREATE INDEX IF NOT EXISTS \"{Escape(indexName)}\" ON {Qualify(sidecarSchema, sidecarTable)} USING gin (\"{Escape(groupColumn)}\");\n";

    public static string DropGroupIndex(string indexName) =>
        $"DROP INDEX IF EXISTS \"{Escape(indexName)}\";\n";

    public static string DropGroupColumn(string sidecarTable, string? sidecarSchema, string groupColumn) =>
        $"ALTER TABLE {Qualify(sidecarSchema, sidecarTable)} DROP COLUMN IF EXISTS \"{Escape(groupColumn)}\";\n";

    public static string CreateSyncFunctionAndTrigger(
        string sidecarTable,
        string? sidecarSchema,
        string sourceTable,
        string? sourceSchema,
        string idColumn,
        string functionName,
        string triggerName,
        string columnSuffix,
        IReadOnlyList<FullTextGroup> groups) =>
        CreateSyncFunctionAndTrigger(sidecarTable, sidecarSchema, sourceTable, sourceSchema,
            new[] { idColumn }, functionName, triggerName, columnSuffix, groups);

    public static string CreateSyncFunctionAndTrigger(
        string sidecarTable,
        string? sidecarSchema,
        string sourceTable,
        string? sourceSchema,
        IReadOnlyList<string> keyColumns,
        string functionName,
        string triggerName,
        string columnSuffix,
        IReadOnlyList<FullTextGroup> groups)
    {
        if (groups.Count == 0)
        {
            return DropSyncFunctionAndTrigger(sourceTable, sourceSchema, functionName, triggerName);
        }

        var sidecar = Qualify(sidecarSchema, sidecarTable);
        var source  = Qualify(sourceSchema,  sourceTable);
        var escapedKeys = keyColumns.Select(Escape).ToList();
        var keyList = string.Join(", ", escapedKeys.Select(k => $"\"{k}\""));

        // The owner sync trigger can only reference NEW.* owner columns, so it
        // composes owner-local properties only. Joined properties are maintained
        // by the reindex backfill (enqueued via change-tracking triggers). A group
        // with no owner-local properties is left untouched by this trigger.
        var groupColumns = groups
            .Select(g => (
                Group: g,
                Column: Escape(g.Name + columnSuffix),
                LocalProps: g.Properties.Where(p => p.Join is null).ToList()))
            .Where(g => g.LocalProps.Count > 0)
            .ToList();

        if (groupColumns.Count == 0)
        {
            return DropSyncFunctionAndTrigger(sourceTable, sourceSchema, functionName, triggerName);
        }

        // Collect all distinct owner-local source columns, sorted deterministically
        var allColumns = groupColumns
            .SelectMany(g => g.LocalProps.Select(p => p.ColumnName))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder();

        // ── Function ──────────────────────────────────────────────────────────
        sb.AppendLine($"CREATE OR REPLACE FUNCTION \"{Escape(functionName)}\"() RETURNS trigger LANGUAGE plpgsql AS $$");
        sb.AppendLine("BEGIN");

        // INSERT INTO sidecar (...) VALUES (...) ON CONFLICT DO UPDATE SET ...
        sb.Append($"    INSERT INTO {sidecar} (").Append(keyList);
        foreach (var (_, col, _) in groupColumns)
            sb.Append($", \"{col}\"");
        sb.AppendLine(", \"updated_at\")");

        sb.Append("    VALUES (")
          .Append(string.Join(", ", escapedKeys.Select(k => $"NEW.\"{k}\"")));
        foreach (var (group, _, localProps) in groupColumns)
            sb.Append(", ").Append(BuildGroupExpression(group, localProps));
        sb.AppendLine(", now())");

        sb.Append("    ON CONFLICT (").Append(keyList).AppendLine(") DO UPDATE SET");
        for (var i = 0; i < groupColumns.Count; i++)
        {
            var col = groupColumns[i].Column;
            sb.Append($"        \"{col}\" = EXCLUDED.\"{col}\"");
            sb.AppendLine(",");
        }
        sb.AppendLine("        \"updated_at\" = now();");

        sb.AppendLine("    RETURN NEW;");
        sb.AppendLine("END;");
        sb.AppendLine("$$;");

        // ── Trigger ───────────────────────────────────────────────────────────
        var updateOfCols = string.Join(", ", allColumns.Select(c => $"\"{Escape(c)}\""));
        sb.AppendLine($"CREATE TRIGGER \"{Escape(triggerName)}\"");
        sb.AppendLine($"AFTER INSERT OR UPDATE OF {updateOfCols}");
        sb.AppendLine($"ON {source}");
        sb.AppendLine("FOR EACH ROW EXECUTE FUNCTION");
        sb.AppendLine($"\"{Escape(functionName)}\"();");

        return sb.ToString();
    }

    public static string DropSyncFunctionAndTrigger(
        string sourceTable,
        string? sourceSchema,
        string functionName,
        string triggerName)
    {
        var source = Qualify(sourceSchema, sourceTable);
        return $"DROP TRIGGER IF EXISTS \"{Escape(triggerName)}\" ON {source};\n" +
               $"DROP FUNCTION IF EXISTS \"{Escape(functionName)}\"();\n";
    }

    public static string Backfill(
        string sidecarTable, string? sidecarSchema,
        string sourceTable,  string? sourceSchema,
        string idColumn, string columnSuffix,
        IReadOnlyList<FullTextGroup> groups) =>
        Backfill(sidecarTable, sidecarSchema, sourceTable, sourceSchema,
            new[] { idColumn }, columnSuffix, groups);

    public static string Backfill(
        string sidecarTable, string? sidecarSchema,
        string sourceTable,  string? sourceSchema,
        IReadOnlyList<string> keyColumns, string columnSuffix,
        IReadOnlyList<FullTextGroup> groups)
    {
        var sidecar = Qualify(sidecarSchema, sidecarTable);
        var source  = Qualify(sourceSchema,  sourceTable);
        var escapedKeys = keyColumns.Select(Escape).ToList();
        var keyColumnList = string.Join(", ", escapedKeys.Select(k => $"\"{k}\""));
        var columns = groups.Select(g => g.Name + columnSuffix).ToList();
        var composed = ComposeGroups(groups, sourceTable, sourceSchema, keyColumns);

        var sb = new StringBuilder();
        sb.Append("INSERT INTO ").Append(sidecar).Append(" (").Append(keyColumnList).Append(", ");
        sb.Append(string.Join(", ", columns.Select(c => $"\"{Escape(c)}\""))).AppendLine(", \"updated_at\")");
        sb.Append("SELECT ").Append(SelectKeyColumns(composed, keyColumns, escapedKeys)).Append(", ");
        sb.Append(string.Join(", ", composed.Sources.Select(g => BuildGroupSelectExpression(g, composed.HasJoins))));
        sb.AppendLine(", now()");
        sb.Append("FROM ").Append(source);
        AppendComposedTail(sb, composed, keyColumns);
        sb.AppendLine();
        sb.Append("ON CONFLICT (").Append(keyColumnList).AppendLine(") DO UPDATE SET");
        foreach (var col in columns)
        {
            sb.Append("    \"").Append(Escape(col)).Append("\" = EXCLUDED.\"").Append(Escape(col)).AppendLine("\",");
        }
        sb.AppendLine("    \"updated_at\" = now();");
        return sb.ToString();
    }

#pragma warning disable RS0027 // single-key overload keeps greaterThan defaulted for source-compat
    public static string BackfillBatch(
        string sidecarTable, string? sidecarSchema,
        string sourceTable,  string? sourceSchema,
        string idColumn, string columnSuffix,
        IReadOnlyList<FullTextGroup> groups,
        bool greaterThan = true) =>
        BackfillBatch(sidecarTable, sidecarSchema, sourceTable, sourceSchema,
            new[] { idColumn }, columnSuffix, groups, greaterThan);
#pragma warning restore RS0027

    public static string BackfillBatch(
        string sidecarTable, string? sidecarSchema,
        string sourceTable,  string? sourceSchema,
        IReadOnlyList<string> keyColumns, string columnSuffix,
        IReadOnlyList<FullTextGroup> groups,
        bool greaterThan)
    {
        var sidecar = Qualify(sidecarSchema, sidecarTable);
        var source  = Qualify(sourceSchema,  sourceTable);
        var escapedKeys = keyColumns.Select(Escape).ToList();
        var keyColumnList = string.Join(", ", escapedKeys.Select(k => $"\"{k}\""));
        var columns = groups.Select(g => g.Name + columnSuffix).ToList();
        var op = greaterThan ? ">" : ">=";
        var composed = ComposeGroups(groups, sourceTable, sourceSchema, keyColumns);

        var sb = new StringBuilder();
        sb.Append("WITH \"_batch\" AS (").AppendLine();
        sb.Append("    SELECT ").Append(keyColumnList).AppendLine();
        sb.Append("    FROM ").Append(source).AppendLine();
        if (escapedKeys.Count == 1)
        {
            sb.Append("    WHERE \"").Append(escapedKeys[0]).Append("\" ").Append(op).AppendLine(" @last_id");
        }
        else
        {
            sb.Append("    WHERE (").Append(keyColumnList).Append(") ").Append(op)
              .Append(" (").Append(string.Join(", ", escapedKeys.Select((_, i) => $"@last_id{i}"))).AppendLine(")");
        }
        sb.Append("    ORDER BY ").Append(keyColumnList).AppendLine();
        sb.AppendLine("    LIMIT @batch_size");
        sb.AppendLine("), \"_indexed\" AS (");
        sb.Append("INSERT INTO ").Append(sidecar).Append(" (").Append(keyColumnList).Append(", ");
        sb.Append(string.Join(", ", columns.Select(c => $"\"{Escape(c)}\""))).AppendLine(", \"updated_at\")");
        sb.Append("SELECT ").Append(SelectKeyColumns(composed, keyColumns, escapedKeys)).Append(", ");
        sb.Append(string.Join(", ", composed.Sources.Select(g => BuildGroupSelectExpression(g, composed.HasJoins))));
        sb.AppendLine(", now()");
        sb.Append("FROM ").Append(source);
        AppendJoins(sb, composed);
        sb.AppendLine();
        var whereKey = composed.HasJoins ? OwnerQualify(escapedKeys[0]) : $"\"{escapedKeys[0]}\"";
        var whereKeyList = composed.HasJoins
            ? string.Join(", ", escapedKeys.Select(OwnerQualify))
            : keyColumnList;
        if (escapedKeys.Count == 1)
        {
            sb.Append("WHERE ").Append(whereKey).Append(" IN (SELECT \"").Append(escapedKeys[0]).AppendLine("\" FROM \"_batch\")");
        }
        else
        {
            sb.Append("WHERE (").Append(whereKeyList).Append(") IN (SELECT ").Append(keyColumnList).AppendLine(" FROM \"_batch\")");
        }
        AppendGroupBy(sb, composed, keyColumns);
        sb.Append("ON CONFLICT (").Append(keyColumnList).AppendLine(") DO UPDATE SET");
        foreach (var col in columns)
        {
            sb.Append("    \"").Append(Escape(col)).Append("\" = EXCLUDED.\"").Append(Escape(col)).AppendLine("\",");
        }
        sb.AppendLine("    \"updated_at\" = now()");
        sb.Append("RETURNING ").Append(keyColumnList).AppendLine();
        sb.AppendLine(")");
        if (escapedKeys.Count == 1)
        {
            // ORDER BY ... LIMIT 1 instead of max(): max() has no aggregate for
            // some key types (e.g. uuid) but every key type is orderable.
            var k = escapedKeys[0];
            sb.Append("SELECT count(*), (SELECT \"").Append(k)
              .Append("\" FROM \"_indexed\" ORDER BY \"").Append(k).Append("\" DESC LIMIT 1) FROM \"_indexed\";");
        }
        else
        {
            var orderDesc = string.Join(", ", escapedKeys.Select(k => $"\"{k}\" DESC"));
            sb.Append("SELECT count(*), ")
              .Append(string.Join(", ", escapedKeys.Select(k =>
                  $"(SELECT \"{k}\" FROM \"_indexed\" ORDER BY {orderDesc} LIMIT 1)")))
              .Append(" FROM \"_indexed\";");
        }
        return sb.ToString();
    }

    private sealed record ComposedGroup(FullTextGroup Group, ComposedTextSource Source);

    private sealed record ComposedGroups(
        IReadOnlyList<ComposedGroup> Sources,
        IReadOnlyList<ComposedJoin> Joins,
        bool HasJoins);

    private static ComposedGroups ComposeGroups(
        IReadOnlyList<FullTextGroup> groups,
        string ownerTable,
        string? ownerSchema,
        IReadOnlyList<string> ownerKeyColumns)
    {
        var sources = new List<ComposedGroup>(groups.Count);
        var joins = new List<ComposedJoin>();
        var seenAliases = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var src = ComposedTextSource.Build(group, ownerTable, ownerSchema, ownerKeyColumns);
            sources.Add(new ComposedGroup(group, src));
            foreach (var join in src.Joins)
            {
                if (seenAliases.Add(join.Alias))
                    joins.Add(join);
            }
        }

        return new ComposedGroups(sources, joins, joins.Count > 0);
    }

    private static string SelectKeyColumns(
        ComposedGroups composed,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<string> escapedKeys) =>
        composed.HasJoins
            ? string.Join(", ", escapedKeys.Select(OwnerQualify))
            : string.Join(", ", escapedKeys.Select(k => $"\"{k}\""));

    private static void AppendComposedTail(
        StringBuilder sb,
        ComposedGroups composed,
        IReadOnlyList<string> keyColumns)
    {
        AppendJoins(sb, composed);
        if (composed.HasJoins)
            AppendGroupByInline(sb, keyColumns);
    }

    private static void AppendJoins(StringBuilder sb, ComposedGroups composed)
    {
        if (!composed.HasJoins)
            return;

        sb.Append(' ').Append('"').Append(ComposedTextSource.OwnerAlias).Append('"');
        foreach (var join in composed.Joins)
        {
            sb.AppendLine();
            sb.Append("LEFT JOIN ").Append(Qualify(join.Schema, join.TableName))
              .Append(" \"").Append(Escape(join.Alias)).Append("\" ON ").Append(join.OnClause);
        }
    }

    private static void AppendGroupBy(StringBuilder sb, ComposedGroups composed, IReadOnlyList<string> keyColumns)
    {
        if (!composed.HasJoins)
            return;
        sb.Append("GROUP BY ").Append(string.Join(", ", keyColumns.Select(k => OwnerQualify(Escape(k))))).AppendLine();
    }

    private static void AppendGroupByInline(StringBuilder sb, IReadOnlyList<string> keyColumns)
    {
        sb.AppendLine();
        sb.Append("GROUP BY ").Append(string.Join(", ", keyColumns.Select(k => OwnerQualify(Escape(k)))));
    }

    private static string OwnerQualify(string escapedColumn) =>
        $"\"{ComposedTextSource.OwnerAlias}\".\"{escapedColumn}\"";

    private static string BuildGroupSelectExpression(ComposedGroup composed, bool hasJoins)
    {
        var group = composed.Group;
        var parts = composed.Source.Columns.Select(c =>
        {
            var cfg = c.FullTextConfigOverride ?? group.FullTextConfig;
            var label = c.Weight.ToString();
            var inner = c.Kind switch
            {
                ComposedColumnKind.OwnerLocal when !hasJoins =>
                    $"\"{Escape(c.ColumnName)}\"",
                // Owner-local columns are functionally dependent on the grouped
                // owner key, so they stay bare even under GROUP BY.
                ComposedColumnKind.OwnerLocal =>
                    $"\"{Escape(c.Alias)}\".\"{Escape(c.ColumnName)}\"",
                ComposedColumnKind.Aggregated =>
                    $"string_agg(\"{Escape(c.Alias)}\".\"{Escape(c.ColumnName)}\", ' ')",
                // N:1 scalar from a joined table: single-valued per owner but not
                // covered by the GROUP BY functional dependency, so aggregate it.
                _ => $"min(\"{Escape(c.Alias)}\".\"{Escape(c.ColumnName)}\")",
            };
            return $"setweight(to_tsvector('{cfg}', coalesce({inner}, '')), '{label}')";
        });
        return string.Join(" || ", parts);
    }

    private static string BuildGroupExpression(FullTextGroup group, IReadOnlyList<FullTextGroupProperty> properties)
    {
        var parts = properties
            .Select(p =>
            {
                var config = p.FullTextConfigOverride ?? group.FullTextConfig;
                var col    = Escape(p.ColumnName);
                var weight = p.Weight.ToString();
                return $"setweight(to_tsvector('{config}', coalesce(NEW.\"{col}\", '')), '{weight}')";
            });
        return string.Join(" || ", parts);
    }

    private const int MaxHops = 5;

    public static string CreateChangeTrackingFunctionAndTrigger(
        string joinedTable,
        string? joinedSchema,
        string ownerTable,
        string? ownerSchema,
        IReadOnlyList<string> ownerKeyColumns,
        Ferret.Abstractions.Search.JoinPath joinPath,
        string functionName,
        string triggerName,
        string entityName,
        string groupName)
    {
        var hops = joinPath.Hops;
        if (hops.Count == 0 || hops.Count > MaxHops)
            throw new ArgumentException(
                $"Join path must have between 1 and {MaxHops} hops.", nameof(joinPath));

        var joined = Qualify(joinedSchema, joinedTable);
        var owner  = Qualify(ownerSchema,  ownerTable);
        var ownerKeys = ownerKeyColumns.Select(Escape).ToList();
        var entityLit = entityName.Replace("'", "''");
        var groupLit  = groupName.Replace("'", "''");

        var resolveNew = BuildOwnerResolution(hops, owner, ownerKeys, ownerSchema, ownerTable, "NEW");
        var resolveOld = BuildOwnerResolution(hops, owner, ownerKeys, ownerSchema, ownerTable, "OLD");

        // Composite owner keys are '|'-joined into the single text last_id column and
        // decoded by ReindexJobProcessor.DecodeCompositeKey. String key parts may contain
        // '|' or '\', so each part is escaped ('\' -> '\\', '|' -> '\|') BEFORE the join,
        // byte-identical to ReindexJobProcessor.EncodeCompositeKey, so decode round-trips.
        var keyTextExpr = ownerKeys.Count == 1
            ? "_owner_key::text"
            : "concat_ws('|', " + string.Join(", ", ownerKeys.Select((_, i) =>
                $"replace(replace(_owner_key_{i}::text, '\\', '\\\\'), '|', '\\|')")) + ")";

        var sb = new StringBuilder();
        sb.AppendLine($"CREATE OR REPLACE FUNCTION \"{Escape(functionName)}\"() RETURNS trigger LANGUAGE plpgsql AS $$");
        if (ownerKeys.Count == 1)
        {
            sb.AppendLine("DECLARE");
            sb.AppendLine("    _owner_key text;");
        }
        else
        {
            sb.AppendLine("DECLARE");
            foreach (var (_, i) in ownerKeys.Select((k, i) => (k, i)))
                sb.AppendLine($"    _owner_key_{i} text;");
        }
        sb.AppendLine("BEGIN");
        sb.AppendLine("    IF (TG_OP = 'DELETE') THEN");
        AppendResolutionLoop(sb, resolveOld, owner, entityLit, groupLit, keyTextExpr, ownerKeys);
        sb.AppendLine("    ELSE");
        AppendResolutionLoop(sb, resolveNew, owner, entityLit, groupLit, keyTextExpr, ownerKeys);
        sb.AppendLine("    END IF;");
        sb.AppendLine("    RETURN NULL;");
        sb.AppendLine("END;");
        sb.AppendLine("$$;");

        sb.AppendLine($"CREATE TRIGGER \"{Escape(triggerName)}\"");
        sb.AppendLine("AFTER INSERT OR UPDATE OR DELETE");
        sb.AppendLine($"ON {joined}");
        sb.AppendLine("FOR EACH ROW EXECUTE FUNCTION");
        sb.AppendLine($"\"{Escape(functionName)}\"();");

        return sb.ToString();
    }

    public static string DropChangeTrackingFunctionAndTrigger(
        string joinedTable,
        string? joinedSchema,
        string functionName,
        string triggerName)
    {
        var joined = Qualify(joinedSchema, joinedTable);
        return $"DROP TRIGGER IF EXISTS \"{Escape(triggerName)}\" ON {joined};\n" +
               $"DROP FUNCTION IF EXISTS \"{Escape(functionName)}\"();\n";
    }

    private sealed record OwnerResolution(string Query, IReadOnlyList<string> SelectColumns);

    private static OwnerResolution BuildOwnerResolution(
        IReadOnlyList<Ferret.Abstractions.Search.JoinHop> hops,
        string owner,
        IReadOnlyList<string> ownerKeys,
        string? ownerSchema,
        string ownerTable,
        string row)
    {
        var lastHop = hops[^1];

        // Direct 1:N: the changed row carries the owner FK column verbatim.
        if (hops.Count == 1 && !lastHop.ForeignKeyOwningSide)
        {
            var selects = new List<string> { $"{row}.\"{Escape(lastHop.ForeignKeyColumn)}\"" };
            return new OwnerResolution(Query: string.Empty, SelectColumns: selects);
        }

        const string ownerAlias = "o";
        var sb = new StringBuilder();
        sb.Append("SELECT DISTINCT ")
          .Append(string.Join(", ", ownerKeys.Select(k => $"\"{ownerAlias}\".\"{k}\"")))
          .Append(" FROM ").Append(owner).Append(" \"").Append(ownerAlias).Append('"');

        // Alias each intermediate table (all hops except the changed one).
        var aliases = new string[hops.Count];
        for (var i = 0; i < hops.Count; i++)
            aliases[i] = "h" + i;

        var prevAlias = ownerAlias;
        var prevKey = ownerKeys[0];
        for (var i = 0; i < hops.Count - 1; i++)
        {
            var hop = hops[i];
            var alias = aliases[i];
            var table = Qualify(hop.Schema, hop.TableName);
            string on = hop.ForeignKeyOwningSide
                ? $"\"{alias}\".\"{Escape(hop.ReferencedKeyColumn)}\" = \"{prevAlias}\".\"{Escape(hop.ForeignKeyColumn)}\""
                : $"\"{alias}\".\"{Escape(hop.ForeignKeyColumn)}\" = \"{prevAlias}\".\"{Escape(prevKey)}\"";
            sb.Append(" JOIN ").Append(table).Append(" \"").Append(alias).Append("\" ON ").Append(on);
            prevAlias = alias;
            prevKey = hop.ReferencedKeyColumn;
        }

        // Link the last (changed) row to the previous table via the row variable.
        string where = lastHop.ForeignKeyOwningSide
            ? $"\"{prevAlias}\".\"{Escape(lastHop.ForeignKeyColumn)}\" = {row}.\"{Escape(lastHop.ReferencedKeyColumn)}\""
            : $"{row}.\"{Escape(lastHop.ForeignKeyColumn)}\" = \"{prevAlias}\".\"{Escape(prevKey)}\"";
        sb.Append(" WHERE ").Append(where);

        return new OwnerResolution(
            Query: sb.ToString(),
            SelectColumns: ownerKeys.Select(k => $"\"{ownerAlias}\".\"{k}\"").ToList());
    }

    private static void AppendResolutionLoop(
        StringBuilder sb,
        OwnerResolution resolution,
        string owner,
        string entityLit,
        string groupLit,
        string keyTextExpr,
        IReadOnlyList<string> ownerKeys)
    {
        if (resolution.Query.Length == 0)
        {
            // Direct: owner key is a single scalar expression off NEW/OLD.
            var expr = resolution.SelectColumns[0];
            sb.AppendLine($"        IF {expr} IS NOT NULL THEN");
            sb.AppendLine("            INSERT INTO \"" + ReindexJobsTable + "\" (\"entity\", \"group_name\", \"status\", \"batch_size\", \"last_id\")");
            sb.AppendLine($"            SELECT '{entityLit}', '{groupLit}', 'pending', 0, {expr}::text");
            sb.AppendLine("            WHERE NOT EXISTS (");
            sb.AppendLine($"                SELECT 1 FROM \"{ReindexJobsTable}\"");
            sb.AppendLine($"                WHERE \"entity\" = '{entityLit}' AND \"group_name\" = '{groupLit}'");
            sb.AppendLine($"                  AND \"status\" = 'pending' AND \"last_id\" = {expr}::text");
            sb.AppendLine("            );");
            sb.AppendLine("        END IF;");
            return;
        }

        var keyVars = ownerKeys.Count == 1
            ? "_owner_key"
            : string.Join(", ", ownerKeys.Select((_, i) => $"_owner_key_{i}"));

        sb.AppendLine($"        FOR {keyVars} IN");
        sb.AppendLine("            " + resolution.Query);
        sb.AppendLine("        LOOP");
        sb.AppendLine("            INSERT INTO \"" + ReindexJobsTable + "\" (\"entity\", \"group_name\", \"status\", \"batch_size\", \"last_id\")");
        sb.AppendLine($"            SELECT '{entityLit}', '{groupLit}', 'pending', 0, {keyTextExpr}");
        sb.AppendLine("            WHERE NOT EXISTS (");
        sb.AppendLine($"                SELECT 1 FROM \"{ReindexJobsTable}\"");
        sb.AppendLine($"                WHERE \"entity\" = '{entityLit}' AND \"group_name\" = '{groupLit}'");
        sb.AppendLine($"                  AND \"status\" = 'pending' AND \"last_id\" = {keyTextExpr}");
        sb.AppendLine("            );");
        sb.AppendLine("        END LOOP;");
    }

    public const string ReindexJobsTable = "ferret_reindex_jobs";

    public static string EnsureReindexJobsTable() => $"""
        CREATE TABLE IF NOT EXISTS "{ReindexJobsTable}" (
            "id" bigserial PRIMARY KEY,
            "entity" text NOT NULL,
            "group_name" text NOT NULL,
            "status" text NOT NULL,
            "batch_size" int NOT NULL,
            "last_id" text,
            "enqueued_at" timestamptz NOT NULL DEFAULT now(),
            "started_at" timestamptz,
            "finished_at" timestamptz,
            "error" text
        );
        """ + "\n";

    public static string EnqueueReindexJob(string entity, string group, int batchSize)
    {
        var entityLit = entity.Replace("'", "''");
        var groupLit  = group.Replace("'", "''");
        return $"INSERT INTO \"{ReindexJobsTable}\" (\"entity\", \"group_name\", \"status\", \"batch_size\") " +
               $"VALUES ('{entityLit}', '{groupLit}', 'pending', {batchSize});\n";
    }

    private static string Qualify(string? schema, string table) =>
        schema is null ? $"\"{Escape(table)}\"" : $"\"{Escape(schema)}\".\"{Escape(table)}\"";

    private static string Escape(string identifier) => identifier.Replace("\"", "\"\"");
}
