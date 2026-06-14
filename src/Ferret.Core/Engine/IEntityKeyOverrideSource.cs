namespace Ferret.Core.Engine;

/// <summary>
/// Supplies ordered key-property overrides for scanned entities at registry-build
/// time. Lets an adapter (e.g. EF Core) auto-fill a composite key from its own model
/// when the <c>[SearchableEntity]</c> attribute does not name the key explicitly.
/// Core resolves any registered source when building the <see cref="EntityRegistry"/>.
/// </summary>
public interface IEntityKeyOverrideSource
{
    /// <summary>
    /// Returns the ordered key-property names for each entity the source recognises.
    /// Entities absent from the result keep their attribute/default key resolution.
    /// </summary>
    IReadOnlyDictionary<Type, IReadOnlyList<string>> GetKeyOverrides(IEnumerable<Type> entityTypes);
}
