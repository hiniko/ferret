namespace Ferret.AspNetCore;

public enum FerretEndpointPaginationMode
{
    Offset,
    Cursor,
}

public sealed class FerretEndpointOptions
{
    public FerretEndpointPaginationMode Pagination { get; set; } = FerretEndpointPaginationMode.Offset;

    public int? DefaultLimit { get; set; }

    public int? MaxLimit { get; set; }

    public string? Name { get; set; }

    public string? Tag { get; set; }

    public string? Summary { get; set; }
}
