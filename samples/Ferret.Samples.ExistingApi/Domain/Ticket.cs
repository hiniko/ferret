namespace Ferret.Example.ExistingApi.Domain;

/// <summary>
/// A pre-existing entity. The only change made for Ferret is the attributes —
/// no new base class, no interface, no property changes.
/// </summary>
[SearchableEntity]                                    // <-- added for Ferret
public sealed class Ticket : AuditedEntity
{
    [Searchable]                                      // <-- added for Ferret
    [Redacted]                                        // pre-existing, unrelated attribute
    public string Subject { get; set; } = "";

    [Searchable(Weight = 0.5f)]                       // <-- added for Ferret
    public string Body { get; set; } = "";

    [Filterable, Sortable]                            // <-- added for Ferret
    public string Status { get; set; } = "open";

    // Not searchable, not filterable — Ferret ignores it entirely.
    public Guid OrganisationId { get; set; }

    public Organisation Organisation { get; set; } = null!;
}

public sealed class Organisation : AuditedEntity
{
    public string Name { get; set; } = "";
}

/// <summary>Pre-existing attribute from the app's own redaction system.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RedactedAttribute : Attribute;
