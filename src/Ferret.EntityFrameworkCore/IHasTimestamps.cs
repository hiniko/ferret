namespace Ferret.EntityFrameworkCore;

/// <summary>Opt-in: entities that need <c>CrudRepository</c> to maintain CreatedAt/UpdatedAt.</summary>
public interface IHasTimestamps
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}
