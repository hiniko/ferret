using Ferret.Abstractions.Session;

namespace Ferret.Core.Engine.Reindex;

public interface IReindexRunner
{
    Task<int> DrainAsync(IFerretSession session, CancellationToken ct);

    Task<int> DrainAsync(IFerretSession session, ReindexDrainOptions options, CancellationToken ct = default);
}
