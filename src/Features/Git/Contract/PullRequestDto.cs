namespace ABox.Features.Git.Contract;

public sealed record PullRequestDto(int Number, string Title, string State);
