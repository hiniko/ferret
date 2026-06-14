using Microsoft.EntityFrameworkCore;

namespace Ferret.Example.Basic.Seed;

public sealed class ProductSeeder(IServiceProvider sp, ILogger<ProductSeeder> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync(ct);

        if (await db.Products.AnyAsync(ct))
        {
            log.LogInformation("Products already seeded; skipping.");
            return;
        }

        db.Products.AddRange(ProductSeedData.Build());
        await db.SaveChangesAsync(ct);
        log.LogInformation("Seeded {Count} products.", await db.Products.CountAsync(ct));
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

internal static class ProductSeedData
{
    private static readonly string[] Categories = ["tools", "garden", "kitchen", "office", "outdoor"];
    private static readonly string[] Adjectives = ["Blue", "Red", "Green", "Steel", "Brass", "Copper", "Oak", "Pine", "Heavy", "Light"];
    private static readonly string[] Nouns = ["Widget", "Widge", "Hammer", "Spanner", "Trowel", "Kettle", "Notebook", "Lantern", "Bracket", "Clamp"];

    public static IEnumerable<Product> Build()
    {
        var rng = new Random(42);
        var baseDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 200; i++)
        {
            var adj = Adjectives[rng.Next(Adjectives.Length)];
            var noun = Nouns[rng.Next(Nouns.Length)];
            var category = Categories[i % Categories.Length];
            yield return new Product
            {
                Id = Guid.NewGuid(),
                Name = $"{adj} {noun}",
                Sku = $"{category[..3].ToUpperInvariant()}-{i:D4}",
                Category = category,
                Price = Math.Round((decimal)(rng.NextDouble() * 200 + 5), 2),
                Stock = rng.Next(0, 500),
                CreatedAt = baseDate.AddHours(i),
            };
        }
    }
}
