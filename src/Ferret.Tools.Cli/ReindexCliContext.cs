using System.Reflection;
using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Sql;
using Ferret.Core.Configuration;
using Ferret.Core.DependencyInjection;
using Ferret.Core.Engine;
using Ferret.Core.Engine.Reindex;
using Microsoft.Extensions.DependencyInjection;

namespace Ferret.Tools.Cli;

internal sealed class ReindexCliContext
{
    private readonly IReadOnlyList<Assembly> _entityAssemblies;
    private readonly Lazy<ServiceProvider> _provider;

    public ReindexCliContext(IReadOnlyList<Assembly> entityAssemblies)
    {
        _entityAssemblies = entityAssemblies;
        _provider = new Lazy<ServiceProvider>(BuildProvider);
    }

    private ServiceProvider Provider => _provider.Value;

    public EntityRegistry Registry => Provider.GetRequiredService<EntityRegistry>();
    public IReindexRunner Runner => Provider.GetRequiredService<IReindexRunner>();
    public ISqlDialect Dialect => Provider.GetRequiredService<ISqlDialect>();

    public bool TryResolveTable(string entity, out string tableName)
    {
        var type = _entityAssemblies
            .SelectMany(SafeGetTypes)
            .FirstOrDefault(t =>
                (t.IsPublic || t.IsNestedPublic)
                && t.GetCustomAttribute<SearchableEntityAttribute>() is not null
                && (string.Equals(t.Name, entity, StringComparison.Ordinal)
                    || string.Equals(t.FullName, entity, StringComparison.Ordinal)));

        if (type is null)
        {
            tableName = "";
            return false;
        }

        tableName = Registry.Get(type).TableName;
        return true;
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFerret(opts =>
        {
            foreach (var asm in _entityAssemblies)
                opts.ScanAssembly(asm);
            opts.UsePostgres();
            opts.UseFullTextSearch();
        });
        return services.BuildServiceProvider();
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
