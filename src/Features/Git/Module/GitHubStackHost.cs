using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ABox.Domain.Git;

namespace ABox.Features.Git.Module;

// provisional: real GitHub adapter, unverified without a token (S2.2b verify-later) — the fake is the tested default.
internal sealed class GitHubStackHost : IStackHost
{
    private readonly HttpClient _http;
    private readonly GitHubOptions _options;

    public GitHubStackHost(HttpClient http, GitHubOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Token))
            throw new InvalidOperationException(
                "GitHubStackHost requires a GitHub token. Set GitHub:Token in configuration, or leave it unset to use the in-memory fake.");

        _http = http;
        _options = options;
        _http.BaseAddress ??= new Uri("https://api.github.com/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ABox-Agent");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    private string Repo => $"repos/{_options.Owner}/{_options.Repo}";

    public async Task<BranchRef> CreateBranch(string name, string fromRef, CancellationToken ct)
    {
        var fromSha = await ResolveSha(fromRef, ct);
        var res = await Post($"{Repo}/git/refs", new { @ref = $"refs/heads/{name}", sha = fromSha }, ct);
        var body = await Read<RefBody>(res, ct);
        return new BranchRef(name, body.Object.Sha);
    }

    public async Task DeleteBranch(string name, CancellationToken ct)
    {
        var res = await _http.DeleteAsync($"{Repo}/git/refs/heads/{name}", ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task<PullRef> OpenPullRequest(string head, string baseRef, string title, CancellationToken ct)
    {
        var res = await Post($"{Repo}/pulls", new { head, @base = baseRef, title }, ct);
        var body = await Read<PullBody>(res, ct);
        return new PullRef(body.Number, body.Head.Ref, body.Base.Ref);
    }

    public async Task RetargetPullRequest(int number, string newBaseRef, CancellationToken ct)
    {
        var res = await Patch($"{Repo}/pulls/{number}", new { @base = newBaseRef }, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task<MergeOutcome> Merge(int number, CancellationToken ct)
    {
        // merge_method is locked to a merge commit for stacked nodes (the-box.md §16); never squash/rebase.
        var res = await Put($"{Repo}/pulls/{number}/merge", new { merge_method = "merge" }, ct);
        if (res.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.Conflict)
            return new MergeOutcome("", false);
        var body = await Read<MergeBody>(res, ct);
        return new MergeOutcome(body.Sha, body.Merged);
    }

    public async Task<PullView> GetPullRequest(int number, CancellationToken ct)
    {
        var res = await _http.GetAsync($"{Repo}/pulls/{number}", ct);
        var body = await Read<PullBody>(res, ct);
        var mergeable = body.MergeableState is "clean" or "unstable" or "has_hooks";
        return new PullView(number, body.Base.Ref, body.State, mergeable);
    }

    private async Task<string> ResolveSha(string fromRef, CancellationToken ct)
    {
        var res = await _http.GetAsync($"{Repo}/git/ref/heads/{fromRef}", ct);
        var body = await Read<RefBody>(res, ct);
        return body.Object.Sha;
    }

    private Task<HttpResponseMessage> Post(string url, object body, CancellationToken ct) =>
        _http.PostAsJsonAsync(url, body, ct);

    private Task<HttpResponseMessage> Patch(string url, object body, CancellationToken ct) =>
        _http.PatchAsJsonAsync(url, body, ct);

    private Task<HttpResponseMessage> Put(string url, object body, CancellationToken ct) =>
        _http.PutAsJsonAsync(url, body, ct);

    private static async Task<T> Read<T>(HttpResponseMessage res, CancellationToken ct)
    {
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<T>(ct))
            ?? throw new InvalidOperationException("GitHubStackHost: empty response body");
    }

    private sealed record RefBody([property: JsonPropertyName("object")] RefObject Object);
    private sealed record RefObject([property: JsonPropertyName("sha")] string Sha);

    private sealed record PullBody(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("mergeable_state")] string? MergeableState,
        [property: JsonPropertyName("head")] PullSide Head,
        [property: JsonPropertyName("base")] PullSide Base);

    private sealed record PullSide([property: JsonPropertyName("ref")] string Ref);

    private sealed record MergeBody(
        [property: JsonPropertyName("sha")] string Sha,
        [property: JsonPropertyName("merged")] bool Merged);
}
