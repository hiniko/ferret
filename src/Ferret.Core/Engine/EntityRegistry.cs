namespace Ferret.Core.Engine;

public sealed class EntityRegistry
{
    private readonly Dictionary<Type, EntityModel> _models;

    private EntityRegistry(Dictionary<Type, EntityModel> models) => _models = models;

    public static EntityRegistry Build(IEnumerable<Type> entityTypes, INamingStrategy naming) =>
        Build(entityTypes, naming, (FullTextResolverDefaults?)null, keyOverrides: null);

    internal static EntityRegistry Build(
        IEnumerable<Type> entityTypes,
        INamingStrategy naming,
        FullTextResolverDefaults? fullTextDefaults) =>
        Build(entityTypes, naming, fullTextDefaults, keyOverrides: null);

    public static EntityRegistry Build(
        IEnumerable<Type> entityTypes,
        INamingStrategy naming,
        IReadOnlyDictionary<Type, IReadOnlyList<string>>? keyOverrides) =>
        Build(entityTypes, naming, null, keyOverrides);

    internal static EntityRegistry Build(
        IEnumerable<Type> entityTypes,
        INamingStrategy naming,
        FullTextResolverDefaults? fullTextDefaults,
        IReadOnlyDictionary<Type, IReadOnlyList<string>>? keyOverrides)
    {
        var dict = new Dictionary<Type, EntityModel>();
        foreach (var t in entityTypes)
        {
            IReadOnlyList<string>? keyOverride = null;
            keyOverrides?.TryGetValue(t, out keyOverride);
            dict[t] = EntityModelBuilder.Build(t, naming, fullTextDefaults, keyOverride);
        }
        return new EntityRegistry(dict);
    }

    internal IEnumerable<EntityModel> All => _models.Values;

    public EntityModel Get<T>() where T : class => Get(typeof(T));

    public EntityModel Get(Type clrType) =>
        _models.TryGetValue(clrType, out var m)
            ? m
            : throw new InvalidOperationException(
                $"Entity {clrType.Name} is not registered. Add [SearchableEntity] and call services.AddFerret(opts => opts.ScanAssembly(...))");
}
