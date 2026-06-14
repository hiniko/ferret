using System;
using System.Reflection;
using Ferret.Abstractions.Embeddings;
using Ferret.Abstractions.Querying;
using Ferret.Core.Backends.FullText;
using Ferret.Core.Backends.Hybrid;
using Ferret.Core.Backends.Vector;
using Ferret.Core.Engine.Reindex;
using Ferret.Core.Querying;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ferret.Core.DependencyInjection;

public static class FerretServiceCollectionExtensions
{
    public static IServiceCollection AddFerret(this IServiceCollection services, Action<FerretOptions> configure)
    {
        var builder = new FerretBuilder(services);
        configure(builder.Options);

        services.AddSingleton<INamingStrategy>(_ =>
            (INamingStrategy)Activator.CreateInstance(builder.Options.NamingStrategyType)!);
        services.AddSingleton<ISqlDialect, PostgresDialect>();

        EntityRegistry BuildRegistry(IServiceProvider sp)
        {
            var naming = sp.GetRequiredService<INamingStrategy>();
            var entityTypes = builder.Options.ScannedAssemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => (t.IsPublic || t.IsNestedPublic) && t.GetCustomAttribute<SearchableEntityAttribute>() is not null)
                .ToList();
            var ftDefaults = new FullTextResolverDefaults(
                builder.Options.FullText.DefaultConfig,
                builder.Options.FullText.DefaultReindex,
                builder.Options.FullText.WeightBuckets.A,
                builder.Options.FullText.WeightBuckets.B,
                builder.Options.FullText.WeightBuckets.C);

            // Adapters (e.g. EF Core) may auto-fill composite keys the attribute does
            // not name. Merge every registered source; declaration order is preserved.
            var overrides = new Dictionary<Type, IReadOnlyList<string>>();
            foreach (var source in sp.GetServices<IEntityKeyOverrideSource>())
            {
                foreach (var (type, keys) in source.GetKeyOverrides(entityTypes))
                    overrides[type] = keys;
            }

            return EntityRegistry.Build(entityTypes, naming, ftDefaults,
                overrides.Count > 0 ? overrides : null);
        }

        services.AddSingleton(BuildRegistry);

        if (builder.Options.TrigramEnabled)
        {
            services.AddSingleton(builder.Options.Trigram);
            services.AddSingleton<ISearchBackend>(sp =>
                new TrigramSearchBackend(sp.GetRequiredService<ISqlDialect>(), sp.GetRequiredService<TrigramOptions>()));
        }

        if (builder.Options.FullTextEnabled)
        {
            services.AddSingleton(builder.Options.FullText);
            services.AddSingleton<ISearchBackend>(sp =>
                new FullTextSearchBackend(
                    sp.GetRequiredService<ISqlDialect>(),
                    sp.GetRequiredService<FullTextOptions>()));
            services.AddSingleton<IReindexRunner>(sp =>
                new ReindexRunner(
                    sp.GetRequiredService<EntityRegistry>(),
                    sp.GetRequiredService<FullTextOptions>(),
                    sp.GetService<ILogger<ReindexRunner>>()));
        }

        if (builder.Options.VectorEnabled)
        {
            services.AddSingleton(builder.Options.Vector);
            builder.Options.Vector.ApplyHttpClientRegistration(services);

            var factory = builder.Options.Vector.EmbeddingProviderFactory
                ?? throw new InvalidOperationException(
                    "UseVectorSearch requires an embedding provider. Call UseVectorSearch(v => v.UseEmbeddingProvider(...)).");
            services.AddSingleton<IEmbeddingProvider>(sp => factory(sp));

            services.AddSingleton<ISearchBackend>(sp =>
                new VectorSearchBackend(
                    sp.GetRequiredService<ISqlDialect>(),
                    sp.GetRequiredService<VectorOptions>(),
                    sp.GetRequiredService<IEmbeddingProvider>()));
        }

        if (builder.Options.HybridEnabled)
        {
            services.AddSingleton(builder.Options.Hybrid);
        }

        services.AddSingleton(new FerretRuntimeOptions
        {
            SlowQueryThresholdMs = builder.Options.SlowQueryThresholdMs,
            LogStatements = builder.Options.LogStatements,
        });
        services.AddSingleton<IFerretEngine, FerretEngine>();
        services.AddScoped<IFerretQueryService, FerretCoreQueryService>();

        services.Configure<PaginationOptions>(o =>
        {
            o.DefaultLimit = builder.Options.DefaultLimit;
            o.MaxLimit = builder.Options.MaxLimit;
        });

        return services;
    }
}
