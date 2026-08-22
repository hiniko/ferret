using System.Globalization;

namespace Ferret.Abstractions.Models;

/// <summary>
/// Builders for <see cref="FilterClause"/>. <see cref="FilterClause.Value"/> is a string that
/// Ferret converts to the target property's CLR type, so values are formatted with the invariant
/// culture here — hand-formatting a <see cref="DateTime"/> or <see cref="decimal"/> under a
/// non-invariant culture is an easy way to produce a filter that silently matches nothing.
/// </summary>
public static class Filters
{
    /// <summary>Field equals value.</summary>
    public static FilterClause Eq(string field, object? value) => Clause(field, FilterOperator.Equals, value);

    /// <summary>Field does not equal value.</summary>
    public static FilterClause Neq(string field, object? value) => Clause(field, FilterOperator.NotEquals, value);

    /// <summary>Field is greater than value.</summary>
    public static FilterClause Gt(string field, object? value) => Clause(field, FilterOperator.GreaterThan, value);

    /// <summary>Field is greater than or equal to value.</summary>
    public static FilterClause Gte(string field, object? value) => Clause(field, FilterOperator.GreaterThanOrEqual, value);

    /// <summary>Field is less than value.</summary>
    public static FilterClause Lt(string field, object? value) => Clause(field, FilterOperator.LessThan, value);

    /// <summary>Field is less than or equal to value.</summary>
    public static FilterClause Lte(string field, object? value) => Clause(field, FilterOperator.LessThanOrEqual, value);

    /// <summary>Field contains the value as a substring (case-insensitive).</summary>
    public static FilterClause Contains(string field, string value) => Clause(field, FilterOperator.Contains, value);

    /// <summary>Field matches any of the supplied values.</summary>
    public static FilterClause In(string field, params object?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
            throw new ArgumentException("In requires at least one value.", nameof(values));

        return new FilterClause
        {
            Field = Require(field),
            Operator = FilterOperator.In,
            Value = string.Join(',', values.Select(Format)),
        };
    }

    /// <summary>Field is NULL.</summary>
    public static FilterClause IsNull(string field) =>
        new() { Field = Require(field), Operator = FilterOperator.IsNull };

    /// <summary>Field is not NULL.</summary>
    public static FilterClause NotNull(string field) =>
        new() { Field = Require(field), Operator = FilterOperator.NotNull };

    private static FilterClause Clause(string field, FilterOperator op, object? value) =>
        new() { Field = Require(field), Operator = op, Value = Format(value) };

    private static string Require(string field) =>
        string.IsNullOrWhiteSpace(field)
            ? throw new ArgumentException("Filter field must be non-empty.", nameof(field))
            : field;

    private static string Format(object? value) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
