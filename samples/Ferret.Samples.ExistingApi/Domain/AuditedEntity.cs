using Ferret.Abstractions.Attributes;

namespace Ferret.Example.ExistingApi.Domain;

/// <summary>
/// Pre-existing base class. Ferret imposes no base type — entities only need
/// `class` and a `notnull` key. This one is here to prove that.
/// </summary>
public abstract class AuditedEntity
{
    public Guid Id { get; init; }
    public DateTime CreatedAt { get; init; }
    // Filterable so soft-delete is enforced inside the search query.
    [Filterable]
    public bool IsDeleted { get; set; }
}
