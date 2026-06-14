using System.Text.Json;
using EntityFrameworkCore.ExtensibleMigrations;
using Ferret.Migrations.Annotations;
using Ferret.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Ferret.Migrations.Handlers;

[CustomMigrationHandler(Order = 100)]
public sealed class VectorMigrationOperationHandler : IMigrationOperationHandler
{
    public IReadOnlyList<MigrationOperation> GetOperations(
        IRelationalModel? source,
        IRelationalModel? target,
        IReadOnlyList<MigrationOperation> existingOperations)
    {
        var ops = new List<MigrationOperation>();
        var sourceGroups = ReadGroups(source);
        var targetGroups = ReadGroups(target);
        var needsExtension = false;

        foreach (var (entity, targetDto) in targetGroups)
        {
            sourceGroups.TryGetValue(entity, out var sourceDto);
            var sourceByName = (sourceDto?.Groups ?? new List<VectorGroupDto>()).ToDictionary(g => g.Name, StringComparer.Ordinal);
            var targetByName = targetDto.Groups.ToDictionary(g => g.Name, StringComparer.Ordinal);

            if (sourceDto is null && targetDto.Groups.Count > 0)
            {
                needsExtension = true;
                ops.Add(new EnsureVectorSidecarTableOperation
                {
                    SidecarTable = targetDto.SidecarTable,
                    SidecarSchema = targetDto.SidecarSchema,
                    SourceTable = targetDto.SourceTable,
                    SourceSchema = targetDto.SourceSchema,
                    IdColumn = targetDto.IdColumn,
                    IdColumnType = targetDto.IdColumnType,
                });
            }

            foreach (var (name, targetGroup) in targetByName)
            {
                if (sourceByName.ContainsKey(name)) continue;
                needsExtension = true;
                ops.Add(new CreateVectorIndexOperation
                {
                    Entity = entity,
                    SidecarTable = targetDto.SidecarTable,
                    SidecarSchema = targetDto.SidecarSchema,
                    SourceTable = targetDto.SourceTable,
                    SourceSchema = targetDto.SourceSchema,
                    IdColumn = targetDto.IdColumn,
                    ColumnSuffix = targetDto.ColumnSuffix,
                    Group = targetGroup.ToDomain(),
                    HnswM = targetDto.HnswM,
                    HnswEfConstruction = targetDto.HnswEfConstruction,
                    ConcurrentBatchSize = 1000,
                });
            }

            foreach (var (name, sourceGroup) in sourceByName)
            {
                if (targetByName.ContainsKey(name)) continue;
                ops.Add(new DropVectorIndexOperation
                {
                    Entity = entity,
                    SidecarTable = sourceDto!.SidecarTable,
                    SidecarSchema = sourceDto.SidecarSchema,
                    ColumnSuffix = sourceDto.ColumnSuffix,
                    Group = sourceGroup.ToDomain(),
                });
            }
        }

        foreach (var (entity, sourceDto) in sourceGroups)
        {
            if (targetGroups.ContainsKey(entity)) continue;
            foreach (var group in sourceDto.Groups)
                ops.Add(new DropVectorIndexOperation
                {
                    Entity = entity,
                    SidecarTable = sourceDto.SidecarTable,
                    SidecarSchema = sourceDto.SidecarSchema,
                    ColumnSuffix = sourceDto.ColumnSuffix,
                    Group = group.ToDomain(),
                });
        }

        if (needsExtension
            && !existingOperations.Any(o => o is EnsurePgvectorExtensionOperation)
            && !ops.Any(o => o is EnsurePgvectorExtensionOperation))
            ops.Insert(0, new EnsurePgvectorExtensionOperation());

        if (ops.OfType<CreateVectorIndexOperation>().Any()
            && !existingOperations.Any(o => o is EnsureReindexJobsTableOperation)
            && !ops.Any(o => o is EnsureReindexJobsTableOperation))
        {
            var extensionIdx = ops.FindIndex(o => o is EnsurePgvectorExtensionOperation);
            ops.Insert(extensionIdx >= 0 ? extensionIdx + 1 : 0, new EnsureReindexJobsTableOperation());
        }

        // Insert the version registry table right after the pgvector extension op (BeforeCore).
        // It lands ahead of the reindex-jobs table; order is immaterial since neither references the other.
        if (ops.OfType<CreateVectorIndexOperation>().Any()
            && !existingOperations.Any(o => o is EnsureVectorVersionRegistryOperation)
            && !ops.Any(o => o is EnsureVectorVersionRegistryOperation))
        {
            var extIdx = ops.FindIndex(o => o is EnsurePgvectorExtensionOperation);
            ops.Insert(extIdx >= 0 ? extIdx + 1 : 0, new EnsureVectorVersionRegistryOperation { Schema = null });
        }

        return ops;
    }

    public bool HasDifferences(
        IRelationalModel? source,
        IRelationalModel? target,
        bool defaultHasDifferences)
    {
        if (defaultHasDifferences) return true;
        var sourceGroups = ReadGroups(source);
        var targetGroups = ReadGroups(target);
        if (sourceGroups.Count != targetGroups.Count) return true;
        foreach (var (entity, targetDto) in targetGroups)
        {
            if (!sourceGroups.TryGetValue(entity, out var sourceDto)) return true;
            if (sourceDto.Groups.Count != targetDto.Groups.Count) return true;
            var sourceByName = sourceDto.Groups.ToDictionary(g => g.Name, StringComparer.Ordinal);
            foreach (var g in targetDto.Groups)
            {
                if (!sourceByName.TryGetValue(g.Name, out var prev)) return true;
                if (JsonSerializer.Serialize(prev) != JsonSerializer.Serialize(g)) return true;
            }
        }
        return false;
    }

    private static Dictionary<string, VectorEntityGroupsDto> ReadGroups(IRelationalModel? model)
    {
        var result = new Dictionary<string, VectorEntityGroupsDto>(StringComparer.Ordinal);
        if (model is null) return result;
        foreach (var entityType in model.Model.GetEntityTypes())
        {
            var annotation = entityType.FindAnnotation(SearchableAnnotationKeys.VectorGroupsV1);
            if (annotation?.Value is not string json) continue;
            var dto = JsonSerializer.Deserialize<VectorEntityGroupsDto>(json);
            if (dto is not null) result[entityType.ClrType.FullName ?? entityType.Name] = dto;
        }
        return result;
    }
}
