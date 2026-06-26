namespace ABox.Features.Projects.Api;

public sealed record UpdateProjectRequest(Guid Id, string Name, string Path);
