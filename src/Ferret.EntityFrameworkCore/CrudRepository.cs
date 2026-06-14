using Microsoft.EntityFrameworkCore;

namespace Ferret.EntityFrameworkCore;

public class CrudRepository<T, TKey>
    where T : class
    where TKey : notnull
{
    private readonly DbContext _context;

    public CrudRepository(DbContext context) => _context = context;

    public Task<T?> GetAsync(TKey id, CancellationToken ct = default) =>
        _context.Set<T>().FirstOrDefaultAsync(e => EF.Property<TKey>(e, "Id")!.Equals(id), ct);

    public async Task<T> CreateAsync(T entity, CancellationToken ct = default)
    {
        if (entity is IHasTimestamps t)
        {
            var now = DateTime.UtcNow;
            t.CreatedAt = now;
            t.UpdatedAt = now;
        }
        _context.Add(entity);
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<T> UpdateAsync(T entity, CancellationToken ct = default)
    {
        if (entity is IHasTimestamps t) t.UpdatedAt = DateTime.UtcNow;
        _context.Update(entity);
        await _context.SaveChangesAsync(ct);
        return entity;                                                              // single round-trip; no follow-up Get
    }

    public async Task DeleteAsync(T entity, CancellationToken ct = default)
    {
        _context.Remove(entity);
        await _context.SaveChangesAsync(ct);
    }
}
