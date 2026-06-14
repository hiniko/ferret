using System.Reflection;

namespace Ferret.Core.Engine;

internal static class EntityModelBuilder
{
    // Reaffirmed at 5 by the benchmark findings in
    // docs/superpowers/specs/2026-05-31-searchjoin-bench-findings.md ("HopBudget Recommendation"):
    // depth 5 is the last point before the fan-out cost knee. Pinned by HopBudgetTuningTests —
    // update that test and the findings doc together if this value ever changes.
    private const int HopBudget = 5;

    public static EntityModel Build(Type clrType, INamingStrategy naming) =>
        Build(clrType, naming, null);

    public static EntityModel Build(
        Type clrType,
        INamingStrategy naming,
        FullTextResolverDefaults? fullTextDefaults) =>
        Build(clrType, naming, fullTextDefaults, keyPropertyOverride: null);

    public static EntityModel Build(
        Type clrType,
        INamingStrategy naming,
        FullTextResolverDefaults? fullTextDefaults,
        IReadOnlyList<string>? keyPropertyOverride)
    {
        var entityAttr = clrType.GetCustomAttribute<SearchableEntityAttribute>();
        var ignored = clrType.GetCustomAttribute<SearchIgnoreAttribute>()?.PropertyNames.ToHashSet(StringComparer.OrdinalIgnoreCase)
                     ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var key = ResolveKey(clrType, entityAttr, naming, keyPropertyOverride);

        var columns = new Dictionary<string, string>(StringComparer.Ordinal);
        var clrTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
        var filterable = new Dictionary<string, FilterableAttribute>(StringComparer.Ordinal);
        var sortable = new HashSet<string>(StringComparer.Ordinal);

        foreach (var prop in clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (ignored.Contains(prop.Name)) continue;
            if (IsCollection(prop.PropertyType)) continue;

            var col = prop.GetCustomAttribute<SearchColumnAttribute>()?.Name ?? naming.ColumnName(prop);
            columns[prop.Name] = col;
            clrTypes[prop.Name] = prop.PropertyType;

            var f = prop.GetCustomAttribute<FilterableAttribute>();
            if (f is not null) filterable[prop.Name] = f;
            if (prop.GetCustomAttribute<SortableAttribute>() is not null) sortable.Add(prop.Name);
        }

        var searchables = new List<SearchablePropertyInfo>();
        DiscoverSearchables(clrType, ignored, naming, currentDepth: 0, currentPath: [], naming.TableName(clrType), searchables);

        fullTextDefaults ??= FullTextResolverDefaults.BuiltIn;
        var fullTextGroups = ResolveFullTextGroups(clrType, searchables, fullTextDefaults, out var fullTextGroupRenames);
        var vectorGroups = ResolveVectorGroups(clrType, searchables);
        var hybridConfig = ResolveHybridConfig(clrType, searchables);

        return new EntityModel
        {
            ClrType = clrType,
            TableName = entityAttr?.Table ?? naming.TableName(clrType),
            Schema = entityAttr?.Schema,
            Key = key,
            ColumnByPropertyName = columns,
            ClrTypeByPropertyName = clrTypes,
            SearchableProperties = searchables,
            FullTextGroups = fullTextGroups,
            FullTextGroupRenames = fullTextGroupRenames,
            VectorGroups = vectorGroups,
            HybridConfig = hybridConfig,
            Filterable = filterable,
            Sortable = sortable,
        };
    }

    private static IReadOnlyList<KeyPart> ResolveKey(
        Type clrType,
        SearchableEntityAttribute? entityAttr,
        INamingStrategy naming,
        IReadOnlyList<string>? keyPropertyOverride)
    {
        var keyProperties = entityAttr?.KeyProperties;
        var keyProperty = entityAttr?.KeyProperty ?? "Id";
        var keyPropertySetExplicitly = entityAttr is not null
            && entityAttr.KeyPropertyExplicitlySet
            && !string.Equals(keyProperty, "Id", StringComparison.Ordinal);

        if (keyProperties is { Length: > 0 } && keyPropertySetExplicitly)
        {
            throw new InvalidOperationException(
                $"Entity {clrType.Name} sets both KeyProperty and KeyProperties. They are mutually exclusive.");
        }

        // An EF-supplied key override is used only when the attribute does not name a key
        // explicitly (neither KeyProperties nor a non-default KeyProperty). This lets EF
        // composite primary keys auto-fill the key list without an attribute.
        string[] names;
        if (keyProperties is { Length: > 0 })
            names = keyProperties;
        else if (keyPropertySetExplicitly)
            names = [keyProperty];
        else if (keyPropertyOverride is { Count: > 0 })
            names = keyPropertyOverride.ToArray();
        else
            names = [keyProperty];

        var parts = new List<KeyPart>(names.Length);
        foreach (var name in names)
        {
            var prop = clrType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Entity {clrType.Name} has no public '{name}' property.");

            parts.Add(new KeyPart
            {
                PropertyName = prop.Name,
                ColumnName = prop.GetCustomAttribute<SearchColumnAttribute>()?.Name ?? naming.ColumnName(prop),
                ClrType = prop.PropertyType,
            });
        }

        return parts;
    }

    private static void DiscoverSearchables(
        Type type,
        HashSet<string> ignored,
        INamingStrategy naming,
        int currentDepth,
        IReadOnlyList<JoinHop> currentPath,
        string ownerTableName,
        List<SearchablePropertyInfo> sink)
    {
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (ignored.Contains(prop.Name)) continue;

            foreach (var sa in prop.GetCustomAttributes<SearchableAttribute>())
            {
                if (IsCollection(prop.PropertyType)) continue;
                sink.Add(new SearchablePropertyInfo
                {
                    Property = prop,
                    Backend = sa.Backend,
                    Weight = sa.Weight,
                    FullTextConfig = sa.FullTextConfig,
                    EmbeddingDimensions = sa.EmbeddingDimensions,
                    EmbeddingSource = sa.EmbeddingSource,
                    Group = string.IsNullOrWhiteSpace(sa.Group) ? "default" : sa.Group,
                    PreviousGroup = string.IsNullOrWhiteSpace(sa.PreviousGroup) ? null : sa.PreviousGroup,
                    JoinPath = new JoinPath { Hops = currentPath },
                    ColumnName = prop.GetCustomAttribute<SearchColumnAttribute>()?.Name ?? naming.ColumnName(prop),
                    OwnerTableName = ownerTableName,
                });
            }

            var join = prop.GetCustomAttribute<SearchJoinAttribute>();
            if (join is null) continue;

            var isCollection = IsCollection(prop.PropertyType);
            var relatedType = isCollection ? GetCollectionElementType(prop.PropertyType) : prop.PropertyType;
            if (relatedType is null) continue;

            var newDepth = currentDepth + join.Depth;
            if (newDepth > HopBudget)
            {
                throw new InvalidOperationException(
                    $"Search-join chain on {type.Name}.{prop.Name} exceeds aggregate hop budget of {HopBudget}.");
            }

            var referencedKey = ReferencedKeyColumn(relatedType, type, prop, naming);

            var hop = isCollection
                ? new JoinHop
                {
                    TableName = naming.TableName(relatedType),
                    TableAlias = AliasFromName(prop.Name) + newDepth,
                    ForeignKeyColumn = join.ForeignKey ?? CollectionForeignKeyColumn(type, prop, naming),
                    EntityType = relatedType,
                    Cardinality = JoinCardinality.OneToMany,
                    ForeignKeyOwningSide = false,
                    Schema = SchemaOf(relatedType),
                    ReferencedKeyColumn = referencedKey,
                }
                : new JoinHop
                {
                    TableName = naming.TableName(relatedType),
                    TableAlias = AliasFromName(prop.Name) + newDepth,
                    ForeignKeyColumn = join.ForeignKey ?? ReferenceForeignKeyColumn(type, prop, naming),
                    EntityType = relatedType,
                    Cardinality = JoinCardinality.ManyToOne,
                    ForeignKeyOwningSide = true,
                    Schema = SchemaOf(relatedType),
                    ReferencedKeyColumn = referencedKey,
                };

            DiscoverSearchables(
                relatedType, ignored, naming, newDepth,
                [..currentPath, hop], hop.TableName, sink);
        }
    }

    private static string? SchemaOf(Type entityType) =>
        entityType.GetCustomAttribute<SearchableEntityAttribute>()?.Schema;

    // The PK column of the table a hop joins into. N:1 joins target this column;
    // multi-hop paths also link through it. Composite keys are out of scope for
    // joined groups, and an entity with no resolvable single key is an error (§7).
    private static string ReferencedKeyColumn(
        Type relatedType, Type ownerType, PropertyInfo navProp, INamingStrategy naming)
    {
        var entityAttr = relatedType.GetCustomAttribute<SearchableEntityAttribute>();

        if (entityAttr?.KeyProperties is { Length: > 1 })
        {
            throw new InvalidOperationException(
                $"Search-join on {ownerType.Name}.{navProp.Name} references {relatedType.Name}, " +
                "which has a composite primary key. Composite keys are not supported for joined search groups.");
        }

        var keyName = entityAttr?.KeyProperties is { Length: 1 } single
            ? single[0]
            : entityAttr?.KeyProperty ?? "Id";

        var keyProp = relatedType.GetProperty(keyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"Search-join on {ownerType.Name}.{navProp.Name} references {relatedType.Name}, " +
                $"which has no resolvable key property '{keyName}'. " +
                "Set [SearchableEntity(KeyProperty = ...)] on the referenced entity.");

        return keyProp.GetCustomAttribute<SearchColumnAttribute>()?.Name ?? naming.ColumnName(keyProp);
    }

    private static string ReferenceForeignKeyColumn(Type ownerType, PropertyInfo navProp, INamingStrategy naming)
    {
        var fkProp = ownerType.GetProperty(navProp.Name + "Id", BindingFlags.Public | BindingFlags.Instance);
        return fkProp is not null
            ? naming.ColumnName(fkProp)
            : naming.ColumnName(navProp) + "_id";
    }

    private static string CollectionForeignKeyColumn(Type ownerType, PropertyInfo navProp, INamingStrategy naming)
    {
        var idProp = ownerType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
        if (idProp is null)
        {
            throw new InvalidOperationException(
                $"Search-join on {ownerType.Name}.{navProp.Name} cannot resolve a reverse-FK column: " +
                $"{ownerType.Name} has no 'Id' property and no ForeignKey override. " +
                "Set [SearchJoin(ForeignKey = ...)] to name the foreign-key column explicitly.");
        }
        return naming.ColumnName(idProp);
    }

    private static bool IsCollection(Type t) =>
        t != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(t);

    private static Type? GetCollectionElementType(Type t)
    {
        if (t.IsGenericType && t.GetGenericArguments().Length == 1) return t.GetGenericArguments()[0];
        return t.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?
            .GetGenericArguments()[0];
    }

    private static string AliasFromName(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in name)
            if (char.IsUpper(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.Length > 0 ? sb.ToString() : name[..Math.Min(2, name.Length)].ToLowerInvariant();
    }

    private static IReadOnlyList<FullTextGroup> ResolveFullTextGroups(
        Type clrType,
        IReadOnlyList<SearchablePropertyInfo> searchables,
        FullTextResolverDefaults defaults,
        out IReadOnlyDictionary<string, string?> groupRenames)
    {
        var groupAttrs = clrType.GetCustomAttributes<SearchGroupAttribute>()
            .ToDictionary(a => a.Name, StringComparer.Ordinal);

        var fullTextProps = searchables.Where(s => s.Backend == SearchBackend.FullText).ToList();
        if (fullTextProps.Count == 0)
        {
            groupRenames = new Dictionary<string, string?>(StringComparer.Ordinal);
            return [];
        }

        var byGroup = fullTextProps
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Group) ? "default" : p.Group, StringComparer.Ordinal);

        var result = new List<FullTextGroup>();
        var renames = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var grp in byGroup)
        {
            groupAttrs.TryGetValue(grp.Key, out var ga);

            var perPropConfigs = grp.Select(p => p.FullTextConfig).Where(c => c is not null).Distinct().ToList();
            if (perPropConfigs.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Fulltext group '{grp.Key}' on '{clrType.Name}' has conflicting FullTextConfig values: " +
                    string.Join(", ", perPropConfigs));
            }

            var perPropPrevGroups = grp.Select(p => p.PreviousGroup).Where(g => g is not null).Distinct().ToList();
            if (perPropPrevGroups.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Fulltext group '{grp.Key}' on '{clrType.Name}' has conflicting PreviousGroup values: " +
                    string.Join(", ", perPropPrevGroups));
            }

            var previousGroup = perPropPrevGroups.FirstOrDefault();
            if (previousGroup is not null && string.Equals(previousGroup, grp.Key, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Fulltext group '{grp.Key}' on '{clrType.Name}' declares PreviousGroup equal to its own name.");
            }

            if (previousGroup is not null)
            {
                renames[grp.Key] = previousGroup;
            }

            var config = perPropConfigs.FirstOrDefault()
                         ?? ga?.FullTextConfig
                         ?? defaults.Config;

            var reindex = ga?.Reindex ?? defaults.Reindex;

            var props = grp.Select(p => new FullTextGroupProperty
            {
                PropertyName = p.Property.Name,
                ColumnName = p.ColumnName,
                Weight = FullTextWeightBucketMapper.Bucket(p.Weight, defaults.BucketA, defaults.BucketB, defaults.BucketC),
                FullTextConfigOverride = p.FullTextConfig is null || p.FullTextConfig == config ? null : p.FullTextConfig,
                Join = p.JoinPath.IsDirect ? null : p.JoinPath,
            }).ToList();

            result.Add(new FullTextGroup
            {
                Name = grp.Key,
                FullTextConfig = config,
                Reindex = reindex,
                Properties = props,
            });
        }
        groupRenames = renames;
        return result;
    }

    private static HybridConfig? ResolveHybridConfig(
        Type clrType,
        IReadOnlyList<SearchablePropertyInfo> searchables)
    {
        var backends = searchables.Select(s => s.Backend).Distinct().ToList();
        if (backends.Count <= 1) return null;

        var attrs = clrType.GetCustomAttributes<HybridBackendAttribute>()
            .ToDictionary(a => a.Backend);

        var list = backends.Select(b =>
        {
            attrs.TryGetValue(b, out var a);
            return new HybridBackendConfig
            {
                Backend = b,
                Weight = a?.Weight ?? double.NaN,
                ConfidenceThreshold = a?.ConfidenceThreshold ?? double.NaN,
            };
        }).ToList();

        return new HybridConfig { Backends = list };
    }

    private static IReadOnlyList<VectorGroup> ResolveVectorGroups(
        Type clrType,
        IReadOnlyList<SearchablePropertyInfo> searchables)
    {
        var vectorProps = searchables.Where(s => s.Backend == SearchBackend.Vector).ToList();
        if (vectorProps.Count == 0) return [];

        var byGroup = vectorProps
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Group) ? "default" : p.Group, StringComparer.Ordinal);

        var result = new List<VectorGroup>();
        foreach (var grp in byGroup)
        {
            var dims = grp.Select(p => p.EmbeddingDimensions).Distinct().ToList();
            if (dims.Count > 1)
                throw new InvalidOperationException(
                    $"Vector group '{grp.Key}' on '{clrType.Name}' has conflicting EmbeddingDimensions values: {string.Join(", ", dims)}");
            if (dims[0] <= 0)
                throw new InvalidOperationException(
                    $"Vector group '{grp.Key}' on '{clrType.Name}' must declare EmbeddingDimensions > 0.");

            result.Add(new VectorGroup
            {
                Name = grp.Key,
                Dimensions = dims[0],
                Properties = grp.Select(p => new VectorGroupProperty
                {
                    PropertyName = p.Property.Name,
                    ColumnName = p.ColumnName,
                    EmbeddingSource = string.IsNullOrWhiteSpace(p.EmbeddingSource) ? p.Property.Name : p.EmbeddingSource!,
                    Join = p.JoinPath.IsDirect ? null : p.JoinPath,
                }).ToList(),
            });
        }
        return result;
    }
}

internal sealed record FullTextResolverDefaults(
    string Config,
    Ferret.Abstractions.Attributes.ReindexMode Reindex,
    float BucketA,
    float BucketB,
    float BucketC)
{
    public static FullTextResolverDefaults BuiltIn { get; } =
        new("simple", Ferret.Abstractions.Attributes.ReindexMode.Inline, 2.0f, 1.0f, 0.5f);
}
