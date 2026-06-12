namespace ABox.Features.Git.Contracts;

public sealed record PullRequestDto(int Number, string Title, string State);
