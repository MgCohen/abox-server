namespace ABox.Features.Tasks.Contracts;

public sealed record TaskDto(int Id, string Title, IReadOnlyList<int> LinkedPullRequests);
