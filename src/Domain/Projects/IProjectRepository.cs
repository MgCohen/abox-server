using ABox.Infrastructure.Storage;

namespace ABox.Domain.Projects;

public interface IProjectRepository : IRepository<Project>
{
    Task<Project?> GetByName(string name, CancellationToken ct = default);
}
