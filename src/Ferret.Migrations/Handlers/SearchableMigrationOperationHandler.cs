using System.Text.Json;
using EntityFrameworkCore.ExtensibleMigrations;
using Ferret.Abstractions;
using Ferret.Abstractions.Search;
using Ferret.Migrations.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Ferret.Migrations.Handlers;

[CustomMigrationHandler(Order = 100)]
public sealed class SearchableMigrationOperationHandler : IMigrationOperationHandler
{
    public IReadOnlyList<MigrationOperation> GetOperations(
        IRelationalModel? source,
        IRelationalModel? target,
        IReadOnlyList<MigrationOperation> existingOperations)
    {
        var sourceIndices = ReadIndices(source);
        var targetIndices = ReadIndices(target);
        var operations = new List<MigrationOperation>();

        // Drops: source - target
        foreach (var (indexName, _) in sourceIndices)
        {
            if (!targetIndices.ContainsKey(indexName))
            {
                operations.Add(new DropSearchableIndexOperation { IndexName = indexName });
            }
        }

        // Creates and replacements
        foreach (var (indexName, def) in targetIndices)
        {
            if (!sourceIndices.TryGetValue(indexName, out var prev))
            {
                operations.Add(new CreateSearchableIndexOperation
                {
                    IndexName = def.IndexName,
                    TableName = def.TableName,
                    ColumnName = def.ColumnName,
                    IndexSql = def.IndexSql,
                });
                continue;
            }

            if (!string.Equals(prev.IndexSql, def.IndexSql, StringComparison.Ordinal))
            {
                operations.Add(new DropSearchableIndexOperation { IndexName = indexName });
                operations.Add(new CreateSearchableIndexOperation
                {
                    IndexName = def.IndexName,
                    TableName = def.TableName,
                    ColumnName = def.ColumnName,
                    IndexSql = def.IndexSql,
                });
            }
        }

        // Extensions: emit one Ensure op per newly required extension when at least one
        // CreateSearchableIndexOperation is being added in this migration.
        if (operations.OfType<CreateSearchableIndexOperation>().Any())
        {
            var sourceExtensions = ReadExtensions(source);
            var targetExtensions = ReadExtensions(target);
            var newExtensions = targetExtensions.Except(sourceExtensions).ToList();
            var alreadyHandled = existingOperations.OfType<EnsurePgTrgmExtensionOperation>()
                .Select(o => o.ExtensionName)
                .Concat(operations.OfType<EnsurePgTrgmExtensionOperation>().Select(o => o.ExtensionName))
                .ToHashSet(StringComparer.Ordinal);

            for (var i = newExtensions.Count - 1; i >= 0; i--)
            {
                if (alreadyHandled.Contains(newExtensions[i])) continue;
                operations.Insert(0, new EnsurePgTrgmExtensionOperation { ExtensionName = newExtensions[i] });
            }
        }

        // Fulltext group diff
        var sourceGroups = ReadFullTextGroups(source);
        var targetGroups = ReadFullTextGroups(target);
        var ftOps = new List<MigrationOperation>();

        foreach (var (entityKey, sourceDto) in sourceGroups)
        {
            if (!targetGroups.ContainsKey(entityKey))
            {
                // Entity removed — drop all its groups
                var allGroupsAfter = Array.Empty<FullTextGroup>();
                foreach (var g in sourceDto.Groups)
                {
                    ftOps.Add(new DropFullTextGroupOperation
                    {
                        SidecarTable = sourceDto.SidecarTable,
                        SidecarSchema = sourceDto.SidecarSchema,
                        SourceTable = sourceDto.SourceTable,
                        SourceSchema = sourceDto.SourceSchema,
                        IdColumn = sourceDto.IdColumn,
                        KeyColumns = KeyColumnsOf(sourceDto),
                        ColumnSuffix = sourceDto.ColumnSuffix,
                        GroupName = g.Name,
                        AllGroupsAfter = allGroupsAfter,
                    });
                }
            }
        }

        foreach (var (entityKey, targetDto) in targetGroups)
        {
            var allGroupsAfter = targetDto.Groups.Select(g => g.ToDomain()).ToList();

            if (!sourceGroups.TryGetValue(entityKey, out var sourceDto))
            {
                // New entity — ensure sidecar table, then create each group
                ftOps.Add(new EnsureSidecarTableOperation
                {
                    SidecarTable = targetDto.SidecarTable,
                    SidecarSchema = targetDto.SidecarSchema,
                    SourceTable = targetDto.SourceTable,
                    SourceSchema = targetDto.SourceSchema,
                    IdColumn = targetDto.IdColumn,
                    IdColumnType = targetDto.IdColumnType,
                    KeyParts = targetDto.KeyParts
                        .Select(k => new EnsureSidecarTableOperation.KeyPart
                        {
                            ColumnName = k.ColumnName,
                            ColumnType = k.ColumnType,
                        })
                        .ToList(),
                });

                foreach (var g in targetDto.Groups)
                {
                    var group = g.ToDomain();
                    ftOps.Add(new CreateFullTextGroupOperation
                    {
                        Entity = entityKey,
                        SidecarTable = targetDto.SidecarTable,
                        SidecarSchema = targetDto.SidecarSchema,
                        SourceTable = targetDto.SourceTable,
                        SourceSchema = targetDto.SourceSchema,
                        IdColumn = targetDto.IdColumn,
                        KeyColumns = KeyColumnsOf(targetDto),
                        ColumnSuffix = targetDto.ColumnSuffix,
                        Group = group,
                        AllGroupsAfter = allGroupsAfter,
                        ReindexMode = g.Reindex,
                    });
                }
            }
            else
            {
                // Both have it — diff by group name
                var sourceByName = sourceDto.Groups.ToDictionary(g => g.Name, StringComparer.Ordinal);
                var targetByName = targetDto.Groups.ToDictionary(g => g.Name, StringComparer.Ordinal);

                foreach (var (groupName, sourceGroup) in sourceByName)
                {
                    if (!targetByName.ContainsKey(groupName))
                    {
                        ftOps.Add(new DropFullTextGroupOperation
                        {
                            SidecarTable = targetDto.SidecarTable,
                            SidecarSchema = targetDto.SidecarSchema,
                            SourceTable = targetDto.SourceTable,
                            SourceSchema = targetDto.SourceSchema,
                            IdColumn = targetDto.IdColumn,
                            KeyColumns = KeyColumnsOf(targetDto),
                            ColumnSuffix = targetDto.ColumnSuffix,
                            GroupName = groupName,
                            AllGroupsAfter = allGroupsAfter,
                        });
                    }
                }

                foreach (var (groupName, targetGroup) in targetByName)
                {
                    if (!sourceByName.TryGetValue(groupName, out var prevGroup))
                    {
                        var group = targetGroup.ToDomain();
                        ftOps.Add(new CreateFullTextGroupOperation
                        {
                            Entity = entityKey,
                            SidecarTable = targetDto.SidecarTable,
                            SidecarSchema = targetDto.SidecarSchema,
                            SourceTable = targetDto.SourceTable,
                            SourceSchema = targetDto.SourceSchema,
                            IdColumn = targetDto.IdColumn,
                            KeyColumns = KeyColumnsOf(targetDto),
                            ColumnSuffix = targetDto.ColumnSuffix,
                            Group = group,
                            AllGroupsAfter = allGroupsAfter,
                            ReindexMode = targetGroup.Reindex,
                        });
                    }
                    else if (!GroupsEqual(prevGroup, targetGroup))
                    {
                        var group = targetGroup.ToDomain();
                        ftOps.Add(new AlterFullTextGroupOperation
                        {
                            Entity = entityKey,
                            SidecarTable = targetDto.SidecarTable,
                            SidecarSchema = targetDto.SidecarSchema,
                            SourceTable = targetDto.SourceTable,
                            SourceSchema = targetDto.SourceSchema,
                            IdColumn = targetDto.IdColumn,
                            KeyColumns = KeyColumnsOf(targetDto),
                            ColumnSuffix = targetDto.ColumnSuffix,
                            Group = group,
                            AllGroupsAfter = allGroupsAfter,
                            ReindexMode = targetGroup.Reindex,
                        });
                    }
                }
            }
        }

        // Joined-table trigger diff: per entity, compute the set of joined tables referenced by
        // any property's JoinPath and emit create/drop trigger ops for added/removed tables.
        foreach (var entityKey in sourceGroups.Keys.Union(targetGroups.Keys, StringComparer.Ordinal))
        {
            sourceGroups.TryGetValue(entityKey, out var sourceDto);
            targetGroups.TryGetValue(entityKey, out var targetDto);

            var sourceTables = JoinedTables(sourceDto);
            var targetTables = JoinedTables(targetDto);
            var owner = targetDto ?? sourceDto;
            if (owner is null) continue;

            foreach (var table in targetTables.Keys.Except(sourceTables.Keys))
            {
                var entry = targetTables[table];
                ftOps.Add(new CreateJoinedTableTriggerOperation
                {
                    Entity = entityKey,
                    SidecarTable = owner.SidecarTable,
                    SidecarSchema = owner.SidecarSchema,
                    SourceTable = owner.SourceTable,
                    SourceSchema = owner.SourceSchema,
                    IdColumn = owner.IdColumn,
                    JoinedTable = entry.Table,
                    JoinedSchema = entry.Schema,
                    GroupName = entry.GroupName,
                    JoinPath = entry.JoinPath,
                });
            }

            foreach (var table in sourceTables.Keys.Except(targetTables.Keys))
            {
                var entry = sourceTables[table];
                ftOps.Add(new DropJoinedTableTriggerOperation
                {
                    Entity = entityKey,
                    SidecarTable = owner.SidecarTable,
                    SidecarSchema = owner.SidecarSchema,
                    SourceTable = owner.SourceTable,
                    SourceSchema = owner.SourceSchema,
                    IdColumn = owner.IdColumn,
                    JoinedTable = entry.Table,
                    JoinedSchema = entry.Schema,
                });
            }
        }

        if (ftOps.Count > 0)
        {
            var needsJobsTable = ftOps.Any(op =>
                op is CreateFullTextGroupOperation c && c.ReindexMode != ReindexMode.Inline ||
                op is AlterFullTextGroupOperation a && a.ReindexMode != ReindexMode.Inline ||
                // A joined-table trigger's body INSERTs into ferret_reindex_jobs, so the
                // table must exist regardless of the group's reindex mode (Inline included).
                op is CreateJoinedTableTriggerOperation);

            if (needsJobsTable)
            {
                ftOps.Insert(0, new EnsureReindexJobsTableOperation());
            }

            operations.AddRange(ftOps);
        }

        return operations;
    }

    public bool HasDifferences(
        IRelationalModel? source,
        IRelationalModel? target,
        bool defaultHasDifferences)
    {
        if (defaultHasDifferences) return true;

        var sourceIndices = ReadIndices(source);
        var targetIndices = ReadIndices(target);

        if (sourceIndices.Count != targetIndices.Count) return true;
        foreach (var (indexName, def) in targetIndices)
        {
            if (!sourceIndices.TryGetValue(indexName, out var prev)) return true;
            if (!string.Equals(prev.IndexSql, def.IndexSql, StringComparison.Ordinal)) return true;
        }

        var sourceGroups = ReadFullTextGroups(source);
        var targetGroups = ReadFullTextGroups(target);

        if (sourceGroups.Count != targetGroups.Count) return true;
        foreach (var (entityKey, targetDto) in targetGroups)
        {
            if (!sourceGroups.TryGetValue(entityKey, out var sourceDto)) return true;
            if (sourceDto.Groups.Count != targetDto.Groups.Count) return true;
            var sourceByName = sourceDto.Groups.ToDictionary(g => g.Name, StringComparer.Ordinal);
            foreach (var g in targetDto.Groups)
            {
                if (!sourceByName.TryGetValue(g.Name, out var prev)) return true;
                if (!GroupsEqual(prev, g)) return true;
            }
        }

        return false;
    }

    private static IReadOnlyDictionary<string, (string Table, string? Schema, JoinPath JoinPath, string GroupName)> JoinedTables(
        FullTextEntityGroupsDto? dto)
    {
        var result = new Dictionary<string, (string, string?, JoinPath, string)>(StringComparer.Ordinal);
        if (dto is null) return result;

        foreach (var group in dto.Groups)
        {
            foreach (var property in group.Properties)
            {
                if (property.Join is null) continue;
                var hops = property.Join.Hops;
                for (var i = 0; i < hops.Count; i++)
                {
                    var hop = hops[i];
                    var key = hop.Schema is null ? hop.TableName : $"{hop.Schema}.{hop.TableName}";
                    var subPath = new JoinPath
                    {
                        Hops = hops.Take(i + 1).Select(h => h.ToDomain()).ToList(),
                    };
                    result[key] = (hop.TableName, hop.Schema, subPath, group.Name);
                }
            }
        }
        return result;
    }

    private static IReadOnlyList<string> KeyColumnsOf(FullTextEntityGroupsDto dto) =>
        dto.KeyParts.Count > 0
            ? dto.KeyParts.Select(k => k.ColumnName).ToList()
            : new List<string> { dto.IdColumn };

    private static bool GroupsEqual(FullTextGroupDto a, FullTextGroupDto b) =>
        string.Equals(
            JsonSerializer.Serialize(a),
            JsonSerializer.Serialize(b),
            StringComparison.Ordinal);

    private static IReadOnlyDictionary<string, FullTextEntityGroupsDto> ReadFullTextGroups(IRelationalModel? model)
    {
        var result = new Dictionary<string, FullTextEntityGroupsDto>(StringComparer.Ordinal);
        if (model is null) return result;

        foreach (var entityType in model.Model.GetEntityTypes())
        {
            var annotation = entityType.FindAnnotation(SearchableAnnotationKeys.FullTextGroupsV1);
            if (annotation?.Value is not string json) continue;
            var dto = JsonSerializer.Deserialize<FullTextEntityGroupsDto>(json);
            if (dto is null) continue;
            var entityKey = entityType.ClrType.FullName ?? entityType.Name;
            result[entityKey] = dto;
        }
        return result;
    }

    private static IReadOnlyDictionary<string, SearchIndexDefinition> ReadIndices(IRelationalModel? model)
    {
        var result = new Dictionary<string, SearchIndexDefinition>(StringComparer.Ordinal);
        if (model is null) return result;

        foreach (var entityType in model.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var annotation = property.FindAnnotation(SearchableAnnotationKeys.SearchableIndexV1);
                if (annotation?.Value is not string json) continue;
                var def = JsonSerializer.Deserialize<SearchIndexDefinition>(json);
                if (def is null) continue;
                result[def.IndexName] = def;
            }
        }
        return result;
    }

    private static IReadOnlySet<string> ReadExtensions(IRelationalModel? model)
    {
        if (model is null) return new HashSet<string>(StringComparer.Ordinal);
        var annotation = model.Model.FindAnnotation(SearchableAnnotationKeys.RequiredExtensionsV1);
        if (annotation?.Value is not string json) return new HashSet<string>(StringComparer.Ordinal);
        var arr = JsonSerializer.Deserialize<string[]>(json) ?? [];
        return arr.ToHashSet(StringComparer.Ordinal);
    }
}
