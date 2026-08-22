namespace Ferret.Example.ExistingApi.Domain;

/// <summary>
/// A pre-existing entity. The only change made for Ferret is the attributes —
/// no new base class, no interface, no property changes.
/// </summary>
[SearchableEntity]                                    // <-- added for Ferret
public sealed class Ticket : AuditedEntity
{
    [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 2.0f)]  // <-- added for Ferret
    [Redacted]                                        // pre-existing, unrelated attribute
    public string Subject { get; set; } = "";

    [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 1.0f)]  // <-- added for Ferret
    public string Body { get; set; } = "";

    [Filterable, Sortable]                            // <-- added for Ferret
    public string Status { get; set; } = "open";

    // Filterable so the server can enforce tenant scoping in the database rather than
    // pre-loading candidate ids. Clients cannot override it — see TicketRepository.Search.
    [Filterable]
    public Guid OrganisationId { get; set; }

    public Organisation Organisation { get; set; } = null!;
}

[SearchableEntity]                                    // <-- added for Ferret: second multisearch arm
public sealed class Organisation : AuditedEntity
{
    [Searchable]                                      // <-- added for Ferret
    public string Name { get; set; } = "";
}

/// <summary>Pre-existing attribute from the app's own redaction system.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RedactedAttribute : Attribute;
