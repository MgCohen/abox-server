namespace ABox.Domain.Git;

public interface IStackHost
{
    Task<BranchRef> CreateBranch(string name, string fromRef, CancellationToken ct);
    Task DeleteBranch(string name, CancellationToken ct);
    Task<PullRef> OpenPullRequest(string head, string baseRef, string title, CancellationToken ct);
    Task RetargetPullRequest(int number, string newBaseRef, CancellationToken ct);
    Task<MergeOutcome> Merge(int number, MergeMethod method, CancellationToken ct);
    Task<PullView> GetPullRequest(int number, CancellationToken ct);
}
