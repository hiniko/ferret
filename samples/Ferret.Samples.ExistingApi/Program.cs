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
    .WithPaginationDefaults(defaultLimit: 25, maxLimit: 100));

builder.Services.AddFerretEntityFrameworkCore<AppDbContext>();

builder.Services.AddHostedService<TicketSeeder>();

var app = builder.Build();
app.MapControllers();
app.Run();
