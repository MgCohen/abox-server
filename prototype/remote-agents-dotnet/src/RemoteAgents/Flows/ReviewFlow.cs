using RemoteAgents.Agents;
using RemoteAgents.Primitives;
using RemoteAgents.Validation;

namespace RemoteAgents.Flows;

// Configuration for a ReviewFlow variant (full-review vs unity-review).
// Captures the differences: which validator gates the work, whether the
// fix loop runs inside an IsolationScope, and the wording handed to the
// reviewer.
public sealed record ReviewFlowOptions(
    string      Name,
    string      ProjectKind,
    string      ValidationLabel,
    bool        IsolateValidation = false,
    string      FixDescriptor     = "",
    int         MaxFixAttempts    = 3,
    string      CoAuthor          = "Claude Opus 4.7 + Codex gpt-5.5");

// Claude works → validate/fix loop → reviewer reviews the diff → optional
// revision pass → commit (push opt-in). One Step per completion boundary.
public sealed class ReviewFlow : Flow
{
    private readonly IAgent             _claude;
    private readonly IAgent             _reviewer;
    private readonly IValidator         _validator;
    private readonly ReviewFlowOptions  _opts;
    private readonly string             _projectDir;
    private readonly string             _prompt;
    private readonly bool               _shouldPush;

    public ReviewFlow(
        IAgent claude, IAgent reviewer, IValidator validator,
        ReviewFlowOptions opts,
        string projectDir, string prompt, bool shouldPush)
    {
        _claude     = claude;
        _reviewer   = reviewer;
        _validator  = validator;
        _opts       = opts;
        _projectDir = projectDir;
        _prompt     = prompt;
        _shouldPush = shouldPush;
    }

    public override string Name => _opts.Name;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Step("guard", async () =>
        {
            if (await GitOps.IsDirtyAsync(_projectDir, ct))
                throw new InvalidOperationException("Working tree is dirty. Commit or stash first.");
        });

        var work = await AgentStep("claude",
            () => _claude.RunAsync(new AgentRunRequest(_prompt, null, _projectDir), ct));

        await using (var iso = _opts.IsolateValidation
            ? await IsolationScope.BeginAsync(_projectDir, ct)
            : null)
        {
            for (var attempt = 1; attempt <= _opts.MaxFixAttempts; attempt++)
            {
                var v = await Step($"validate-{attempt}",
                    () => _validator.ValidateAsync(_projectDir, ct),
                    r => r.Ok ? $"PASSED — {r.Summary}" : $"FAILED — {r.Summary}");
                if (v.Ok) break;
                if (attempt == _opts.MaxFixAttempts)
                    throw new InvalidOperationException(
                        $"Validation never passed after {_opts.MaxFixAttempts} attempts: {v.Summary}");

                var desc = string.IsNullOrEmpty(_opts.FixDescriptor) ? "" : $" ({_opts.FixDescriptor})";
                var fixPrompt = $"Validation{desc} failed. Address these issues:\n\n{v.Errors}";
                work = await AgentStep($"fix-{attempt}",
                    () => _claude.RunAsync(new AgentRunRequest(fixPrompt, work.SessionId, _projectDir), ct));
            }
        }

        // Nothing changed? skip review/commit.
        var diffText = await GitOps.DiffAsync(new GitDiffRequest(_projectDir), ct);
        if (string.IsNullOrWhiteSpace(diffText)) return;

        // Returns parsed ReviewVerdict (not AgentResult), so use Step<T>
        // with both projections to forward Summary and Transcript.
        var verdict = await Step("review",
            () => Reviews.AskAgentForVerdictAsync(_reviewer, _projectDir, _prompt,
                _opts.ProjectKind, _opts.ValidationLabel, ct),
            r => r.Text,
            r => r.Transcript);

        if (verdict.Verdict == Verdict.Unclear)
            throw new InvalidOperationException(
                $"Reviewer verdict unclear (review was {verdict.Text.Length} bytes). Refusing to commit.");

        if (verdict.Verdict == Verdict.Revise)
        {
            work = await AgentStep("revise",
                () => _claude.RunAsync(new AgentRunRequest(
                    $"Code reviewer feedback — please address:\n\n{verdict.Text}",
                    work.SessionId, _projectDir), ct));

            var v2 = await Step("re-validate",
                () => _validator.ValidateAsync(_projectDir, ct),
                r => r.Ok ? $"PASSED — {r.Summary}" : $"FAILED — {r.Summary}");
            if (!v2.Ok)
                throw new InvalidOperationException($"Post-revision validation failed: {v2.Summary}");
        }

        await Step("commit", async () =>
        {
            var files = await GitOps.ChangedFilesAsync(_projectDir, ct);
            if (files.Count == 0) return;
            await GitOps.CommitAsync(new GitCommitRequest(
                ProjectDir: _projectDir,
                Message:    Reviews.BuildCommitMessage(_prompt, verdict.Text, _opts.CoAuthor),
                Files:      files,
                CoAuthor:   _opts.CoAuthor), ct);
        });

        if (_shouldPush)
        {
            await Step("push", async () =>
            {
                var branch = await GitOps.CurrentBranchAsync(_projectDir, ct);
                await GitOps.PushAsync(new GitPushRequest(_projectDir, Branch: branch), ct);
            });
        }
    }
}
