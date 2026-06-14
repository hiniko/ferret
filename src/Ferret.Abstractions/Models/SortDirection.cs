using System.Runtime.Serialization;

namespace Ferret.Abstractions.Models;

public enum SortDirection
{
    [EnumMember(Value = "asc")] Ascending,
    [EnumMember(Value = "desc")] Descending
}
