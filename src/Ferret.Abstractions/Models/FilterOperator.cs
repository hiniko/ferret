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
    [EnumMember(Value = "in")] In
}
