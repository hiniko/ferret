using System.Runtime.Serialization;

namespace Ferret.Abstractions.Models;

public enum FilterOperator
{
    [EnumMember(Value = "eq")] Equals,
    [EnumMember(Value = "neq")] NotEquals,
    [EnumMember(Value = "contains")] Contains,
    [EnumMember(Value = "gt")] GreaterThan,
    [EnumMember(Value = "gte")] GreaterThanOrEqual,
    [EnumMember(Value = "lt")] LessThan,
    [EnumMember(Value = "lte")] LessThanOrEqual,
    [EnumMember(Value = "in")] In,
    /// <summary>Value-less: matches rows where the column is NULL.</summary>
    [EnumMember(Value = "isnull")] IsNull,
    /// <summary>Value-less: matches rows where the column is not NULL.</summary>
    [EnumMember(Value = "notnull")] NotNull
}
