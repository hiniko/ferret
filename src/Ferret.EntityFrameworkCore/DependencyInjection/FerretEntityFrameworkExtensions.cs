using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Querying;
using Ferret.Core.Engine;
using Ferret.EntityFrameworkCore.Querying;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ferret.EntityFrameworkCore.DependencyInjection;

public static class FerretEntityFrameworkExtensions
{
    /// <summary>
    /// Resolves the ordered key-property names for an entity. Honours an explicit
    /// <see cref="SearchableEntityAttribute.KeyProperties"/> (or <see cref="SearchableEntityAttribute.KeyProperty"/>);
    /// otherwise auto-fills from the EF primary key (<see cref="IEntityType.FindPrimaryKey"/>).
    /// </summary>
    public static IReadOnlyList<string> ResolveKeyProperties(IEntityType entityType, SearchableEntityAttribute? attribute)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        if (attribute?.KeyProperties is { Length: > 0 } explicitKeys)
        {
            return explicitKeys;
        }

        if (attribute is not null
            && attribute.KeyPropertyExplicitlySet
            && !string.Equals(attribute.KeyProperty, "Id", StringComparison.Ordinal))
        {
            return [attribute.KeyProperty];
        }

        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"Entity {entityType.ClrType.Name} has no primary key. Configure one via HasKey or set [SearchableEntity(KeyProperties = ...)].");

        return primaryKey.Properties.Select(p => p.Name).ToList();
    }


    public static FerretOptions UseEntityFrameworkCore<TContext>(this FerretOptions options) where TContext : DbContext
    {
        // Marker hook only — actual session is opened per-call by the SearchAsync extension.
        return options;
    }

    public static IServiceCollection AddFerretEntityFrameworkQueryService<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        // Replace (not Add) so the core FerretCoreQueryService registration is removed.
        // Without this, DI ValidateOnBuild fails because FerretCoreQueryService can't
        // resolve IFerretSession (which is per-call, never in DI).
        services.Replace(ServiceDescriptor.Scoped<IFerretQueryService, EntityFrameworkQueryService<TContext>>());
        // Register the key-override source here too: this is the documented entry point,
        // so a composite-PK EF entity that does not name its key via [SearchableEntity]
        // must still auto-fill from the TContext model. TryAddEnumerable de-dupes when
        // AddFerretEntityFrameworkCore<TContext> also runs.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEntityKeyOverrideSource, EntityFrameworkKeyOverrideSource<TContext>>());
        return services;
    }

    /// <summary>
    /// Wires the EF Core query service and registers a key-override source so the
    /// runtime <see cref="EntityRegistry"/> auto-fills composite (and single) keys
    /// from the <typeparamref name="TContext"/> model for scanned entities that do
    /// not name their key via <c>[SearchableEntity(KeyProperties = ...)]</c>.
    /// Call after <c>AddFerret(...)</c> and <c>AddDbContext&lt;TContext&gt;</c>.
    /// </summary>
    public static IServiceCollection AddFerretEntityFrameworkCore<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddFerretEntityFrameworkQueryService<TContext>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEntityKeyOverrideSource, EntityFrameworkKeyOverrideSource<TContext>>());
        return services;
    }
}
