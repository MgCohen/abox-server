namespace ABox.Infrastructure.Storage;

public interface IRepository<T> where T : IEntity
{
    Task<IReadOnlyList<T>> GetAll(CancellationToken ct = default);
    Task<T?> GetById(Guid id, CancellationToken ct = default);
    Task Add(T entity, CancellationToken ct = default);
    Task Update(T entity, CancellationToken ct = default);
    Task Remove(Guid id, CancellationToken ct = default);
}
