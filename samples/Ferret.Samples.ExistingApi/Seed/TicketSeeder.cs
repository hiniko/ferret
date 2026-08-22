using Ferret.Example.ExistingApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ferret.Example.ExistingApi.Seed;

public sealed class TicketSeeder(IServiceProvider sp, ILogger<TicketSeeder> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync(ct);

        if (await db.Tickets.AnyAsync(ct))
        {
            log.LogInformation("Tickets already seeded; skipping.");
            return;
        }

        var (orgs, tickets) = TicketSeedData.Build();
        db.Organisations.AddRange(orgs);
        db.Tickets.AddRange(tickets);
        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "Seeded {Orgs} organisations and {Tickets} tickets ({Deleted} soft-deleted).",
            orgs.Count, tickets.Count, tickets.Count(t => t.IsDeleted));
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

internal static class TicketSeedData
{
    private static readonly string[] Subjects =
    [
        "Printer jams on duplex", "Cannot reset password", "Invoice missing VAT line",
        "Laptop battery draining fast", "VPN disconnects hourly", "Keyboard keys sticking",
        "Monitor flickers at 144hz", "Email bounces to external domains",
        "Shared drive permissions wrong", "Badge reader offline at rear door",
    ];

    private static readonly string[] Bodies =
    [
        "Reported by the finance team this morning, reproducible on demand.",
        "Happens only after the machine has been idle for a while.",
        "Started after last week's rollout; no config changes on our side.",
        "Intermittent, roughly twice a day, no obvious pattern.",
    ];

    private static readonly string[] Statuses = ["open", "pending", "closed"];

    /// <summary>Deterministic seed: two organisations, 60 tickets, some soft-deleted.</summary>
    public static (List<Organisation> Orgs, List<Ticket> Tickets) Build()
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var orgs = new List<Organisation>
        {
            new() { Id = OrgA, Name = "Northwind Ltd", CreatedAt = baseTime },
            new() { Id = OrgB, Name = "Contoso GmbH", CreatedAt = baseTime },
        };

        var tickets = new List<Ticket>();
        for (var i = 0; i < 60; i++)
        {
            tickets.Add(new Ticket
            {
                Id = Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
                Subject = Subjects[i % Subjects.Length],
                Body = Bodies[i % Bodies.Length],
                Status = Statuses[i % Statuses.Length],
                OrganisationId = i % 3 == 0 ? OrgB : OrgA,
                CreatedAt = baseTime.AddHours(i),
                IsDeleted = i % 10 == 9,     // ~10% soft-deleted, must never appear in results
            });
        }

        return (orgs, tickets);
    }

    public static readonly Guid OrgA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid OrgB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
}
