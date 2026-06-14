namespace ABox.Features.Projects.Contracts;

public sealed record UpdateProjectRequest(Guid Id, string Name, string Path);
