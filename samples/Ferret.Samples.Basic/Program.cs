using Ferret.AspNetCore;
using Ferret.Example.Basic;
using Ferret.Example.Basic.Seed;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5433;Database=ferret_samples;Username=postgres;Password=ferret";

builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

builder.Services.AddFerret(opts => opts
    .ScanAssembly(typeof(Product).Assembly)
    .UsePostgres()
    .UseTrigramSearch()
    .WithPaginationDefaults(defaultLimit: 25, maxLimit: 100));

builder.Services.AddFerretAspNetCore();

builder.Services.AddHostedService<ProductSeeder>();

var app = builder.Build();

app.MapControllers();
app.Run();
