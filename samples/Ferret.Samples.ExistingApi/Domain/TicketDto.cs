namespace Ferret.Example.ExistingApi.Domain;

/// <summary>
/// Pre-existing API contract. Ferret results are projected into this — the wire
/// shape does not change because search was added underneath.
/// </summary>
public sealed record TicketDto(Guid Id, string Subject, string Status, string Organisation)
{
    public static TicketDto From(Ticket t) =>
        new(t.Id, t.Subject, t.Status, t.Organisation?.Name ?? "");
}

/// <summary>Pre-existing envelope every list endpoint in this API returns.</summary>
public sealed record ApiPage<T>(IReadOnlyList<T> Data, int Total, int Page, int PerPage);
