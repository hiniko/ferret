using Ferret.Abstractions.Models;
using Ferret.Abstractions.Querying;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Ferret.AspNetCore;

public static class MultiSearchEndpointRouteBuilderExtensions
{
    public static RouteHandlerBuilder MapFerretMultiSearch(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Action<MultiSearchEndpointOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MultiSearchEndpointOptions();
        configure(options);
        if (options.Definitions.Count == 0)
        {
            throw new InvalidOperationException("MapFerretMultiSearch requires at least one Entity<T, THit>() registration.");
        }

        var builder = endpoints.MapGet(pattern, async (HttpContext httpContext, CancellationToken ct) =>
        {
            var qs = httpContext.Request.Query;

            var search = qs["q"].ToString();
            if (string.IsNullOrWhiteSpace(search))
            {
                return Results.Problem(detail: "q is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var limit = options.DefaultPerEntityLimit;
            if (qs.TryGetValue("limit", out var rawLimit) && rawLimit.Count > 0)
            {
                if (!int.TryParse(rawLimit[0], out limit) || limit < 1)
                {
                    return Results.Problem(detail: $"limit must be a positive integer; got '{rawLimit[0]}'.",
                        statusCode: StatusCodes.Status400BadRequest);
                }
                if (limit > options.MaxPerEntityLimit)
                {
                    return Results.Problem(detail: $"limit exceeds maximum ({options.MaxPerEntityLimit})",
                        statusCode: StatusCodes.Status400BadRequest);
                }
            }

            string[]? types = null;
            if (qs.TryGetValue("types", out var rawTypes) && rawTypes.Count > 0)
            {
                types = rawTypes
                    .SelectMany(v => (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .ToArray();
                if (types.Length == 0)
                {
                    types = null;
                }
            }

            var query = new MultiSearchQuery
            {
                Search = search,
                PerEntityLimit = limit,
                Entities = options.Definitions,
                Types = types,
            };

            var service = httpContext.RequestServices.GetRequiredService<IFerretQueryService>();
            try
            {
                var result = await service.MultiSearchAsync(query, ct);
                // Hits are exposed as object so each serializes by its runtime (concrete) hit type;
                // EntityType on the base is the discriminator, no synthetic $type is emitted.
                return Results.Json(new
                {
                    groups = result.Groups.Select(g => new
                    {
                        entityType = g.EntityType,
                        hits = (IEnumerable<object>)g.Hits,
                    }),
                });
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        builder
            .WithName(options.Name ?? "Ferret_MultiSearch")
            .WithTags(options.Tag ?? "Ferret")
            .WithSummary(options.Summary ?? "Search multiple entity types in one call; grouped top-k hits per type.")
            .Produces<MultiSearchResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        if (endpoints.ServiceProvider.GetService<IConfigureOptions<OpenApiOptions>>() is not null)
        {
            builder.AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                operation.Parameters ??= new List<IOpenApiParameter>();
                AddQueryParameter(operation, "q", "Search term.");
                AddQueryParameter(operation, "types", "Comma-separated entity type names to search; defaults to all configured.");
                AddQueryParameter(operation, "limit", "Maximum hits per entity type.");
                return Task.CompletedTask;
            });
        }

        return builder;
    }

    private static void AddQueryParameter(OpenApiOperation operation, string name, string description)
    {
        if (operation.Parameters!.Any(p => string.Equals(p.Name, name, StringComparison.Ordinal)))
        {
            return;
        }

        operation.Parameters!.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Query,
            Required = false,
            Description = description,
        });
    }
}
