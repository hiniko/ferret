using System.Reflection;
using Ferret.Abstractions.Attributes;
using Ferret.Core.Engine;
using Microsoft.EntityFrameworkCore;
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

    public EntityFrameworkKeyOverrideSource(IServiceProvider services) => _services = services;

    public IReadOnlyDictionary<Type, IReadOnlyList<string>> GetKeyOverrides(IEnumerable<Type> entityTypes)
    {
        var result = new Dictionary<Type, IReadOnlyList<string>>();

        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var model = context.Model;

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
}
