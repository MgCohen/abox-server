namespace RemoteAgents.Features.Tasks.Create;

public sealed record TaskDto(int Id, string Title, IReadOnlyList<int> LinkedPullRequests);
