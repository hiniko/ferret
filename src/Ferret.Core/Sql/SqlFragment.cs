namespace Ferret.Core.Sql;

/// <summary>Parameterised SQL plus its binding values. Placeholders use <c>@p0</c>, <c>@p1</c>, …</summary>
public readonly record struct SqlFragment(
    string Sql,
    IReadOnlyList<object?> Parameters,
    int ParameterIndexBase = 0);
