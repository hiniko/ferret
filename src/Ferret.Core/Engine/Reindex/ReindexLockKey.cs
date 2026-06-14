namespace Ferret.Core.Engine.Reindex;

internal static class ReindexLockKey
{
    public static string For(string entity, string group)
        => $"ferret-reindex:{entity}:{group}";
}
