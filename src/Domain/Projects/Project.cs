using ABox.Infrastructure.Storage;

namespace ABox.Domain.Projects;

public sealed record Project(Guid Id, string Name) : IEntity;
