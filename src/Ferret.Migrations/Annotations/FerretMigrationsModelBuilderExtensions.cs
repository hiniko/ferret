using System.Reflection;
using System.Text.Json;
using Ferret.Core.Backends.FullText;
using Ferret.Core.Backends.Trigram;
using Ferret.Core.Backends.Vector;
using Ferret.Core.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Ferret.Migrations.Annotations;

public static class FerretMigrationsModelBuilderExtensions
{
    /// <summary>
    /// Walks the supplied <see cref="EntityRegistry"/>, asks each property's matching backend for
    /// its <see cref="SearchIndexDefinition"/>, and writes the result onto the corresponding EF
    /// <see cref="IMutableProperty"/>. Aggregates required Postgres extensions onto the model root.
    /// Throws <see cref="InvalidOperationException"/> if a searchable property has no matching backend.
    /// </summary>
    public static ModelBuilder UseFerretSearchableAnnotations(
        this ModelBuilder modelBuilder,
        EntityRegistry registry,
        IEnumerable<ISearchBackend> backends)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(backends);

        var backendList = backends.ToList();
        var requiredExtensions = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var efEntity in modelBuilder.Model.GetEntityTypes())
        {
            EntityModel? model;
            try { model = registry.Get(efEntity.ClrType); }
            catch (InvalidOperationException) { continue; }

            foreach (var searchable in model.SearchableProperties)
            {
                // Vector-backed properties are not indexed per-property; they are emitted via the
                // entity-level VectorGroupsV1 annotation (sidecar table + HNSW), mirroring fulltext.
                if (searchable.Backend == SearchBackend.Vector) continue;

                var backend = backendList.FirstOrDefault(b => b.CanHandle(searchable))
                    ?? throw new InvalidOperationException(
                        $"No backend can handle searchable property '{efEntity.ClrType.Name}.{searchable.Property.Name}' (Backend = {searchable.Backend}). Register a matching ISearchBackend.");

                var definition = backend.GetIndexDefinition(searchable);
                if (definition is null) continue;

                var efProperty = efEntity.FindProperty(searchable.Property.Name);
                if (efProperty is null) continue;

                // Resolve the EF-mapped table name and patch any "TBD" placeholder the
                // backend may have used as a deferred-resolution sentinel.
                var tableName = efEntity.GetTableName() ?? definition.TableName;
                if (!string.Equals(tableName, definition.TableName, StringComparison.Ordinal))
                {
                    definition = definition with
                    {
                        IndexName = definition.IndexName.Replace(definition.TableName, tableName, StringComparison.Ordinal),
                        TableName = tableName,
                        IndexSql = definition.IndexSql.Replace(definition.TableName, tableName, StringComparison.Ordinal),
                    };
                }

                efProperty.SetAnnotation(
                    SearchableAnnotationKeys.SearchableIndexV1,
                    JsonSerializer.Serialize(definition));

                foreach (var ext in definition.RequiredExtensions)
                {
                    requiredExtensions.Add(ext);
                }
            }
        }

        if (requiredExtensions.Count > 0)
        {
            modelBuilder.Model.SetAnnotation(
                SearchableAnnotationKeys.RequiredExtensionsV1,
                JsonSerializer.Serialize(requiredExtensions.ToArray()));
        }

        return modelBuilder;
    }

    /// <summary>
    /// Overload that also writes the <see cref="SearchableAnnotationKeys.FullTextGroupsV1"/> entity-level
    /// annotation for every entity whose <see cref="EntityModel"/> carries fulltext groups.
    /// </summary>
    public static ModelBuilder UseFerretSearchableAnnotations(
        this ModelBuilder modelBuilder,
        EntityRegistry registry,
        IEnumerable<ISearchBackend> backends,
        FullTextOptions fullTextOptions)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(backends);
        ArgumentNullException.ThrowIfNull(fullTextOptions);

        modelBuilder.UseFerretSearchableAnnotations(registry, backends);

        foreach (var efEntity in modelBuilder.Model.GetEntityTypes())
        {
            EntityModel? model;
            try { model = registry.Get(efEntity.ClrType); }
            catch (InvalidOperationException) { continue; }

            if (model.FullTextGroups.Count == 0) continue;

            var sourceTable = efEntity.GetTableName() ?? model.TableName;
            var sourceSchema = efEntity.GetSchema() ?? model.Schema;
            var sidecarTable = FullTextSidecarNaming.TableName(sourceTable, fullTextOptions);

            var keyParts = model.Key.Select(part =>
            {
                var efProperty = efEntity.FindProperty(part.PropertyName);
                var columnType = efProperty?.GetColumnType() ?? MapClrTypeToPostgres(efProperty?.ClrType ?? part.ClrType);
                return new FullTextKeyPartDto
                {
                    ColumnName = part.ColumnName,
                    ColumnType = columnType,
                };
            }).ToList();

            var idColumn = keyParts[0].ColumnName;
            var idColumnType = keyParts[0].ColumnType;

            var dto = new FullTextEntityGroupsDto
            {
                SidecarTable = sidecarTable,
                SidecarSchema = fullTextOptions.SidecarSchema ?? sourceSchema,
                SourceTable = sourceTable,
                SourceSchema = sourceSchema,
                IdColumn = idColumn,
                IdColumnType = idColumnType,
                ColumnSuffix = fullTextOptions.ColumnSuffix,
                KeyParts = keyParts,
                Groups = model.FullTextGroups.Select(g =>
                {
                    var groupDto = FullTextGroupDto.FromDomain(g);
                    return model.FullTextGroupRenames.TryGetValue(g.Name, out var previous)
                        ? groupDto with { PreviousGroup = previous }
                        : groupDto;
                }).ToList(),
            };

            efEntity.SetAnnotation(
                SearchableAnnotationKeys.FullTextGroupsV1,
                JsonSerializer.Serialize(dto));
        }

        return modelBuilder;
    }

    /// <summary>
    /// Overload that also writes the <see cref="SearchableAnnotationKeys.FullTextGroupsV1"/> and
    /// <see cref="SearchableAnnotationKeys.VectorGroupsV1"/> entity-level annotations for every
    /// entity whose <see cref="EntityModel"/> carries fulltext groups or vector groups respectively.
    /// </summary>
    public static ModelBuilder UseFerretSearchableAnnotations(
        this ModelBuilder modelBuilder,
        EntityRegistry registry,
        IEnumerable<ISearchBackend> backends,
        FullTextOptions fullTextOptions,
        VectorOptions vectorOptions)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(backends);
        ArgumentNullException.ThrowIfNull(fullTextOptions);
        ArgumentNullException.ThrowIfNull(vectorOptions);

        modelBuilder.UseFerretSearchableAnnotations(registry, backends, fullTextOptions);

        foreach (var efEntity in modelBuilder.Model.GetEntityTypes())
        {
            EntityModel? model;
            try { model = registry.Get(efEntity.ClrType); }
            catch (InvalidOperationException) { continue; }

            if (model.VectorGroups.Count == 0) continue;

            var sourceTable = efEntity.GetTableName() ?? model.TableName;
            var sourceSchema = efEntity.GetSchema() ?? model.Schema;
            var sidecarTable = VectorSidecarNaming.TableName(sourceTable, vectorOptions);

            var keyPart = model.Key[0];
            var keyProp = efEntity.FindProperty(keyPart.PropertyName);
            var idColumnType = keyProp?.GetColumnType() ?? MapClrTypeToPostgres(keyProp?.ClrType ?? keyPart.ClrType);

            var dto = new VectorEntityGroupsDto
            {
                SidecarTable = sidecarTable,
                SidecarSchema = vectorOptions.SidecarSchema ?? sourceSchema,
                SourceTable = sourceTable,
                SourceSchema = sourceSchema,
                IdColumn = keyPart.ColumnName,
                IdColumnType = idColumnType,
                ColumnSuffix = vectorOptions.ColumnSuffix,
                HnswM = vectorOptions.HnswM,
                HnswEfConstruction = vectorOptions.HnswEfConstruction,
                Groups = model.VectorGroups.Select(VectorGroupDto.FromDomain).ToList(),
            };

            efEntity.SetAnnotation(
                SearchableAnnotationKeys.VectorGroupsV1,
                JsonSerializer.Serialize(dto));
        }

        return modelBuilder;
    }

    private static string MapClrTypeToPostgres(Type clrType)
    {
        var t = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (t == typeof(Guid))   return "uuid";
        if (t == typeof(int))    return "integer";
        if (t == typeof(long))   return "bigint";
        if (t == typeof(short))  return "smallint";
        if (t == typeof(string)) return "text";
        throw new NotSupportedException(
            $"Ferret.Migrations cannot derive a Postgres column type for entity key CLR type '{t.FullName}'. " +
            "Set the column type explicitly on the EF property mapping.");
    }

    /// <summary>
    /// Convenience overload — uses the default backend list with default options.
    /// </summary>
    /// <remarks>
    /// Like the assembly overload, this cannot see what <c>AddFerret(...)</c> configured. Safe
    /// only when every backend uses default options; pass backends and options explicitly
    /// otherwise, or use the <c>DbContext</c> overload.
    /// </remarks>
    public static ModelBuilder UseFerretSearchableAnnotations(
        this ModelBuilder modelBuilder,
        EntityRegistry registry)
    {
        var defaultBackends = new ISearchBackend[]
        {
            new TrigramSearchBackend(
                new PostgresDialect(),
                new TrigramOptions()),
            new FullTextSearchBackend(
                new PostgresDialect(),
                new FullTextOptions()),
        };
        return modelBuilder.UseFerretSearchableAnnotations(registry, defaultBackends);
    }

    /// <summary>
    /// Preferred overload. Scans the supplied assembly for <c>[SearchableEntity]</c>-marked
    /// public types and takes the naming strategy, backends and backend options from the
    /// <paramref name="context"/>'s application service provider — i.e. from whatever
    /// <c>AddFerret(...)</c> configured — so generated DDL always matches what queries expect.
    /// </summary>
    /// <remarks>
    /// Call from <c>OnModelCreating</c> as <c>modelBuilder.UseFerretSearchableAnnotations(this, assembly)</c>.
    /// EF's <c>AddDbContext</c> sets the application service provider automatically. When a
    /// context is constructed outside DI, pass the provider to
    /// <c>DbContextOptionsBuilder.UseApplicationServiceProvider(...)</c> or use the
    /// <see cref="UseFerretSearchableAnnotations(ModelBuilder, IServiceProvider, Assembly)"/> overload.
    /// </remarks>
    public static ModelBuilder UseFerretSearchableAnnotations(
        this ModelBuilder modelBuilder,
        DbContext context,
        Assembly entityAssembly)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Design-time tooling (`dotnet ef`) may build the model without an application service
        // provider. Fall back to defaults there rather than failing: this overload must never be
        // a worse choice than the assembly one, only a better-informed one when DI is available.
        var services = context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;

        return services is null
            ? modelBuilder.UseFerretSearchableAnnotations(entityAssembly)
            : modelBuilder.UseFerretSearchableAnnotations(services, entityAssembly);
    }

    /// <summary>
    /// As <see cref="UseFerretSearchableAnnotations(ModelBuilder, DbContext, Assembly)"/>, but takes the
    /// service provider directly — for design-time factories and tests that build a provider by hand.
    /// </summary>
    public static ModelBuilder UseFerretSearchableAnnotations(
        this ModelBuilder modelBuilder,
        IServiceProvider services,
        Assembly entityAssembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(entityAssembly);

        // Backends carry the configured TrigramOptions/FullTextOptions/VectorOptions, and the
        // registry carries the configured naming strategy plus adapter key overrides. Taking
        // them from DI is what keeps generated DDL and query-time SQL in agreement.
        // A provider that has no Ferret registrations (e.g. a design-time host) tells us nothing;
        // defer to the default-options path rather than emitting DDL with no backends at all.
        var backends = services.GetServices<ISearchBackend>().ToList();
        if (backends.Count == 0)
            return modelBuilder.UseFerretSearchableAnnotations(entityAssembly);

        var registry = services.GetService<EntityRegistry>()
            ?? BuildRegistry(modelBuilder, entityAssembly, services.GetService<INamingStrategy>());

        var fullText = services.GetService<FullTextOptions>() ?? new FullTextOptions();
        var vector = services.GetService<VectorOptions>() ?? new VectorOptions();

        // The registry resolved above is the one AddFerret built, so entity/column naming already
        // agrees with query time. Guard the remaining divergence risk: an entity scanned here that
        // the runtime registry does not know about would get DDL no query can ever use.
        if (services.GetService<EntityRegistry>() is not null)
        {
            var unknown = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var efEntity in modelBuilder.Model.GetEntityTypes())
            {
                if (efEntity.ClrType.GetCustomAttribute<SearchableEntityAttribute>() is null) continue;
                try { registry.Get(efEntity.ClrType); }
                catch (InvalidOperationException) { unknown.Add(efEntity.ClrType.Name); }
            }

            if (unknown.Count > 0)
                throw new InvalidOperationException(
                    $"[SearchableEntity] type(s) {string.Join(", ", unknown)} are mapped in this DbContext "
                    + "but are not in the registry AddFerret(...) built, so generated DDL could never be "
                    + "used by a query. Add the assembly with ScanAssembly(...) in AddFerret so migrations "
                    + "and queries agree. See docs/migrations.md.");
        }

        return modelBuilder.UseFerretSearchableAnnotations(registry, backends, fullText, vector);
    }

    /// <summary>
    /// Convenience overload — scans the supplied assembly and uses default backend options.
    /// </summary>
    /// <remarks>
    /// This overload cannot see what <c>AddFerret(...)</c> configured, so it generates DDL for the
    /// DEFAULT trigram/full-text/vector options. That is correct only if the application also uses
    /// defaults. Non-default options (e.g. a custom <c>FullTextOptions.DefaultConfig</c>) produce
    /// DDL that silently disagrees with query-time SQL; the mismatch is caught at first query by
    /// the stamp written here, but prefer
    /// <see cref="UseFerretSearchableAnnotations(ModelBuilder, DbContext, Assembly)"/>, which reads
    /// the real configuration and cannot drift.
    /// </remarks>
#pragma warning disable RS0027 // API with optional parameters differs from other overloads
    public static ModelBuilder UseFerretSearchableAnnotations(
        this ModelBuilder modelBuilder,
        Assembly entityAssembly,
        INamingStrategy? naming = null)
#pragma warning restore RS0027
    {
        ArgumentNullException.ThrowIfNull(entityAssembly);

        var registry = BuildRegistry(modelBuilder, entityAssembly, naming);
        var ftOptions = new FullTextOptions();
        var vectorOptions = new VectorOptions();
        var backends = new ISearchBackend[]
        {
            new TrigramSearchBackend(new PostgresDialect(), new TrigramOptions()),
            new FullTextSearchBackend(new PostgresDialect(), ftOptions),
        };
        return modelBuilder.UseFerretSearchableAnnotations(registry, backends, ftOptions, vectorOptions);
    }

    /// <summary>
    /// Scans <paramref name="entityAssembly"/> and builds a registry, auto-filling keys the
    /// <c>[SearchableEntity]</c> attribute does not name from the EF primary key (composite included).
    /// </summary>
    private static EntityRegistry BuildRegistry(
        ModelBuilder modelBuilder,
        Assembly entityAssembly,
        INamingStrategy? naming)
    {
        var entityTypes = entityAssembly.GetTypes()
            .Where(t => (t.IsPublic || t.IsNestedPublic)
                && t.GetCustomAttribute<SearchableEntityAttribute>() is not null)
            .ToList();

        var keyOverrides = new Dictionary<Type, IReadOnlyList<string>>();
        foreach (var t in entityTypes)
        {
            var efEntity = modelBuilder.Model.FindEntityType(t);
            var pk = efEntity?.FindPrimaryKey();
            if (pk is not null)
                keyOverrides[t] = pk.Properties.Select(p => p.Name).ToList();
        }

        return EntityRegistry.Build(entityTypes, naming ?? new SnakeCaseNamingStrategy(), keyOverrides);
    }
}
