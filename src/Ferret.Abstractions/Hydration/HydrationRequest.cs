namespace Ferret.Abstractions.Hydration;

/// <summary>
/// Hydration request. SQL placeholders are positional <c>{0}</c>, <c>{1}</c>, …
/// matching the order of <c>Parameters</c>. Hydrators may translate to their native syntax.
/// </summary>
public sealed record HydrationRequest(string Sql, IReadOnlyList<object?> Parameters);
