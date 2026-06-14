using Ferret.Abstractions.Models;
using Ferret.Abstractions.Querying;
using Ferret.AspNetCore.Binding;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Ferret.AspNetCore;

public static class FerretEndpointRouteBuilderExtensions
{
    public static RouteHandlerBuilder MapFerret<T, TKey>(this IEndpointRouteBuilder endpoints, string pattern)
        where T : class
        where TKey : notnull
        => endpoints.MapFerret<T, TKey>(pattern, configure: null);

    public static RouteHandlerBuilder MapFerret<T, TKey>(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Action<FerretEndpointOptions>? configure)
        where T : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = new FerretEndpointOptions();
        configure?.Invoke(options);

        var cursor = options.Pagination == FerretEndpointPaginationMode.Cursor;
        var builder = cursor
            ? MapCursor<T, TKey>(endpoints, pattern)
            : MapOffset<T, TKey>(endpoints, pattern);

        if (options.DefaultLimit is not null || options.MaxLimit is not null)
        {
            builder.WithMetadata(new PaginationLimitsAttribute
            {
                Default = options.DefaultLimit ?? 25,
                Max = options.MaxLimit ?? 100,
            });
        }

        builder
            .WithName(options.Name ?? $"Ferret_{typeof(T).Name}")
            .WithTags(options.Tag ?? "Ferret")
            .WithSummary(options.Summary ?? $"Query {typeof(T).Name} with paging, filtering and sorting.")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        if (cursor)
        {
            builder.Produces<CursorResult<T>>(StatusCodes.Status200OK);
        }
        else
        {
            builder.Produces<OffsetResult<T>>(StatusCodes.Status200OK);
        }

        if (HasOpenApiServices(endpoints.ServiceProvider))
        {
            builder.AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                DescribeParameters(operation, cursor);
                return Task.CompletedTask;
            });
        }

        return builder;
    }

    private static bool HasOpenApiServices(IServiceProvider services)
        => services.GetService<IConfigureOptions<OpenApiOptions>>() is not null;

    private static OpenApiOperation DescribeParameters(OpenApiOperation operation, bool cursor)
    {
        operation.Parameters ??= new List<IOpenApiParameter>();

        AddQueryParameter(operation, "q", "Full-text search term.");
        AddQueryParameter(operation, "fields", "Comma-separated list of fields to search.");
        AddQueryParameter(operation, "match_info", "Set to true to include search match metadata.");
        AddQueryParameter(operation, "filter", "Filter clause in field:operator:value form. Repeatable.");
        AddQueryParameter(operation, "sort", "Sort clause in field:direction form. Repeatable.");
        AddQueryParameter(operation, "limit", "Maximum number of items to return.");

        if (cursor)
        {
            AddQueryParameter(operation, "after", "Cursor token to page forward from.");
            AddQueryParameter(operation, "before", "Cursor token to page backward from.");
        }
        else
        {
            AddQueryParameter(operation, "page", "1-based page number.");
            AddQueryParameter(operation, "count", "Set to false to skip computing the total count.");
        }

        return operation;
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

    private static RouteHandlerBuilder MapOffset<T, TKey>(IEndpointRouteBuilder endpoints, string pattern)
        where T : class
        where TKey : notnull
    {
        return endpoints.MapGet(pattern, async (HttpContext httpContext, CancellationToken ct) =>
        {
            var apiQuery = QueryStringQueryBinder.BindOffset(httpContext.Request.Query);

            var resolver = httpContext.RequestServices.GetRequiredService<PaginationDefaultsResolver>();
            var defaults = resolver.Resolve(httpContext);

            PagedQuery<T, TKey> query;
            try
            {
                query = apiQuery.ToPagedQuery<T, TKey>(defaults);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }

            var service = httpContext.RequestServices.GetRequiredService<IFerretQueryService>();
            var result = await service.SearchOffsetAsync(query, ct);
            return Results.Ok(result);
        });
    }

    private static RouteHandlerBuilder MapCursor<T, TKey>(IEndpointRouteBuilder endpoints, string pattern)
        where T : class
        where TKey : notnull
    {
        return endpoints.MapGet(pattern, async (HttpContext httpContext, CancellationToken ct) =>
        {
            var apiQuery = QueryStringQueryBinder.BindCursor(httpContext.Request.Query);

            var resolver = httpContext.RequestServices.GetRequiredService<PaginationDefaultsResolver>();
            var defaults = resolver.Resolve(httpContext);

            PagedQuery<T, TKey> query;
            try
            {
                query = apiQuery.ToPagedQuery<T, TKey>(defaults);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }

            var service = httpContext.RequestServices.GetRequiredService<IFerretQueryService>();
            try
            {
                var result = await service.SearchCursorAsync(query, ct);
                return Results.Ok(result);
            }
            catch (InvalidCursorException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
    }
}
