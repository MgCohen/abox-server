namespace ABox.Features.Git.Contracts;

public interface IPullRequests
{
    IReadOnlyList<PullRequestDto> List(string project);
}
