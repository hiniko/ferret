using Ferret.AspNetCore;
using Ferret.Example.ExistingApi;
using Ferret.Example.ExistingApi.Domain;
using Ferret.Example.ExistingApi.Seed;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5433;Database=ferret_existing;Username=postgres;Password=ferret";

// ---- pre-existing registrations ----
builder.Services.AddControllers();
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddScoped<TicketRepository>();

// ---- added for Ferret: three calls ----
builder.Services.AddFerret(opts => opts
    .ScanAssembly(typeof(Ticket).Assembly)
    .UseTrigramSearch(t => t.MinimumSimilarity = 0.3)
    .UseFullTextSearch(ft => ft.DefaultConfig = "english")
    .WithPaginationDefaults(defaultLimit: 25, maxLimit: 100));

builder.Services.AddFerretEntityFrameworkCore<AppDbContext>();

builder.Services.AddHostedService<TicketSeeder>();

var app = builder.Build();
app.MapControllers();

// Multisearch: type-ahead across tickets (full-text) and organisations (trigram) in one call.
// GET /search?q=printer -> { groups: [ { entityType: "tickets", hits: [...] }, { entityType: "organisations", hits: [...] } ] }
app.MapFerretMultiSearch("/search", o => o
    .WithLimits(defaultPerEntityLimit: 5, maxPerEntityLimit: 25)
    .Entity<Ticket>(t => new Ticket { Id = t.Id, Subject = t.Subject, Status = t.Status })
    .Entity<Organisation>(g => new Organisation { Id = g.Id, Name = g.Name }));

app.Run();
