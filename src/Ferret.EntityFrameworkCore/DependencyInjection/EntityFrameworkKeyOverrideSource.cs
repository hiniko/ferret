using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Ferret.EntityFrameworkCore.DependencyInjection;

/// <summary>
/// Resolves composite (and single) key overrides from a <typeparamref name="TContext"/>
/// EF model so the runtime <see cref="EntityRegistry"/> auto-fills keys for scanned
/// entities that do not name them via <c>[SearchableEntity(KeyProperties = ...)]</c>.
/// </summary>
internal sealed class EntityFrameworkKeyOverrideSource<TContext> : IEntityKeyOverrideSource
    where TContext : DbContext
{
    private readonly IServiceProvider _services;

    // Re-entrancy flag. Reading TContext.Model runs OnModelCreating, and the documented
    // pattern there -- UseFerretSearchableAnnotations(this, assembly) -- resolves
    // EntityRegistry from DI, which re-enters this source before the outer registry build
    // has finished. On re-entry we build the model from an isolated, DI-free context
    // instead (see BuildIsolatedContext), which breaks the cycle.
    //
    // The re-entrant call must still return CORRECT overrides, never a stub: .NET DI has
    // no singleton re-entrancy guard, so the NESTED registry build is the one that gets
    // cached -- whatever this returns on re-entry is what the app keeps.
    private static readonly AsyncLocal<bool> _resolving = new();

    public EntityFrameworkKeyOverrideSource(IServiceProvider services) => _services = services;

    public IReadOnlyDictionary<Type, IReadOnlyList<string>> GetKeyOverrides(IEnumerable<Type> entityTypes)
    {
        var reentrant = _resolving.Value;
        _resolving.Value = true;
        try
        {
            using var scope = _services.CreateScope();

            IModel model;
            if (reentrant)
            {
                var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<TContext>>();
                using var isolated = BuildIsolatedContext(options);
                model = isolated.Model;
            }
            else
            {
                var context = scope.ServiceProvider.GetRequiredService<TContext>();
                model = context.Model;
            }

            var result = new Dictionary<Type, IReadOnlyList<string>>();
            foreach (var clrType in entityTypes)
            {
                var efType = model.FindEntityType(clrType);
                if (efType is null)
                    continue;

                // Honour an explicit attribute key; only auto-fill from the EF primary key
                // when the attribute leaves the key unset. ResolveKeyProperties enforces that
                // a keyed EF entity exists, failing loudly rather than falling back silently.
                var attribute = clrType.GetCustomAttribute<SearchableEntityAttribute>();
                result[clrType] = FerretEntityFrameworkExtensions.ResolveKeyProperties(efType, attribute);
            }

            return result;
        }
        finally
        {
            _resolving.Value = reentrant;
        }
    }

    // Builds a TContext from the same options as the outer call, minus the application
    // service provider, so its OnModelCreating cannot re-enter DI.
    private static TContext BuildIsolatedContext(DbContextOptions<TContext> options)
    {
        var coreExtension = ((DbContextOptions)options).FindExtension<CoreOptionsExtension>()
            ?? new CoreOptionsExtension();
        var strippedOptions = ((DbContextOptions)options).WithExtension(coreExtension.WithApplicationServiceProvider(null));
        return (TContext)Activator.CreateInstance(typeof(TContext), strippedOptions)!;
    }
}
