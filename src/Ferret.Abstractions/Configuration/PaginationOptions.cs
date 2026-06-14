namespace Ferret.Abstractions.Configuration;

public sealed class PaginationOptions
{
    public int DefaultLimit { get; set; } = 25;
    public int MaxLimit { get; set; } = 100;
}
