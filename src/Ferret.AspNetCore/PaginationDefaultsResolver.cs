using Ferret.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Ferret.AspNetCore;

public sealed class PaginationDefaultsResolver
{
    private readonly PaginationOptions _options;

    public PaginationDefaultsResolver(IOptions<PaginationOptions> options) => _options = options.Value;

    public PaginationDefaults Resolve(HttpContext? httpContext)
    {
        var attr = httpContext?.GetEndpoint()?.Metadata.GetMetadata<PaginationLimitsAttribute>();
        if (attr is not null) return new PaginationDefaults(attr.Default, attr.Max);
        return new PaginationDefaults(_options.DefaultLimit, _options.MaxLimit);
    }
}
