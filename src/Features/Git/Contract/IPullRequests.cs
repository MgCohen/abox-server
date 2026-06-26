namespace ABox.Features.Git.Contract;

public interface IPullRequests
{
    IReadOnlyList<PullRequestDto> List(string project);
}
