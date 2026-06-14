
namespace Ferret.Hydration.Dapper.DependencyInjection;

public static class DapperHydrationExtensions
{
    public static FerretOptions UseDapperHydration(this FerretOptions options)
    {
        // Marker hook — `DapperSession` is constructed per-call by the consumer.
        return options;
    }
}
