using ABox.Infrastructure.Storage;

namespace ABox.Domain.Projects;

// Composition over the generic store (06's deferred seam): adds the project-specific GetByName query
// without subclassing the sealed JsonRepository<T>. `inner` is the one IRepository<Project> singleton the
// endpoints bind, so there is a single load-once cache — no coherence hazard. No IO of its own.
public sealed class ProjectRepository(IRepository<Project> inner) : IProjectRepository
{
    public Task<IReadOnlyList<Project>> GetAll(CancellationToken ct = default) => inner.GetAll(ct);
    public Task<Project?> GetById(Guid id, CancellationToken ct = default) => inner.GetById(id, ct);
    public Task Add(Project entity, CancellationToken ct = default) => inner.Add(entity, ct);
    public Task Update(Project entity, CancellationToken ct = default) => inner.Update(entity, ct);
    public Task Remove(Guid id, CancellationToken ct = default) => inner.Remove(id, ct);

    public async Task<Project?> GetByName(string name, CancellationToken ct = default) =>
        (await inner.GetAll(ct)).FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}
