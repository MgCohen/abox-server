namespace ABox.Features.Tasks.Contract;

public sealed record TaskDto(int Id, string Title, IReadOnlyList<int> LinkedPullRequests);
