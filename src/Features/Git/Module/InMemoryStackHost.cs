using System.Collections.Concurrent;
using ABox.Domain.Git;

namespace ABox.Features.Git.Module;

internal sealed class InMemoryStackHost : IStackHost
{
    private readonly ConcurrentDictionary<string, string> _branches = new();
    private readonly ConcurrentDictionary<int, PullState> _pulls = new();
    private int _nextNumber;

    public Task<BranchRef> CreateBranch(string name, string fromRef, CancellationToken ct)
    {
        var sha = SyntheticSha(name);
        _branches[name] = sha;
        if (!_branches.ContainsKey(fromRef))
            _branches[fromRef] = SyntheticSha(fromRef);
        return Task.FromResult(new BranchRef(name, sha));
    }

    public Task DeleteBranch(string name, CancellationToken ct)
    {
        _branches.TryRemove(name, out _);
        return Task.CompletedTask;
    }

    public Task<PullRef> OpenPullRequest(string head, string baseRef, string title, CancellationToken ct)
    {
        var number = Interlocked.Increment(ref _nextNumber);
        _pulls[number] = new PullState(head, baseRef, title);
        return Task.FromResult(new PullRef(number, head, baseRef));
    }

    public Task RetargetPullRequest(int number, string newBaseRef, CancellationToken ct)
    {
        _pulls[number] = Require(number) with { BaseRef = newBaseRef };
        return Task.CompletedTask;
    }

    public Task<MergeOutcome> Merge(int number, CancellationToken ct)
    {
        var pull = Require(number);
        var headSha = _branches.TryGetValue(pull.Head, out var existing) ? existing : SyntheticSha(pull.Head);
        var mergeSha = SyntheticSha($"{pull.BaseRef}+{pull.Head}");
        _branches[pull.BaseRef] = mergeSha;
        _pulls[number] = pull with { State = "merged", MergedHeadSha = headSha, MergedInto = pull.BaseRef };
        return Task.FromResult(new MergeOutcome(mergeSha, true));
    }

    public Task<PullView> GetPullRequest(int number, CancellationToken ct)
    {
        var pull = Require(number);
        var mergeable = pull.State == "open" && _branches.ContainsKey(pull.BaseRef);
        return Task.FromResult(new PullView(number, pull.BaseRef, pull.State, mergeable));
    }

    private PullState Require(int number) =>
        _pulls.TryGetValue(number, out var pull)
            ? pull
            : throw new InvalidOperationException($"InMemoryStackHost: no pull request #{number}");

    private static string SyntheticSha(string seed) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{seed}:{Guid.NewGuid():N}")));

    private sealed record PullState(
        string Head,
        string BaseRef,
        string Title,
        string State = "open",
        string? MergedHeadSha = null,
        string? MergedInto = null);
}
