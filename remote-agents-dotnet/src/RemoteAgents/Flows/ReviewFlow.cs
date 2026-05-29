using RemoteAgents.Agents;
using RemoteAgents.Primitives;
using RemoteAgents.Sessions;
using RemoteAgents.Validation;

namespace RemoteAgents.Flows;

// Claude works → validate/fix loop → Codex reviews the diff → optional
// revision pass → commit (push opt-in). The single body behind both
// `full-review` and `unity-review`: those differ only in which validator
// gates the work, whether the fix loop runs inside an IsolationScope, and
// the wording handed to the reviewer. Everything else was byte-identical.
//
// Construct one per variant (see AgentPresets/the cli shims):
//   new ReviewFlow("full-review",  …, new OrchestratorValidator(), "changes", …)
//   new ReviewFlow("unity-review", …, new UnityFullValidator(), "a Unity C# change",
//                  isolateValidation: true, …)
public sealed class ReviewFlow : IFlow
{
    private readonly IValidator _validator;
    private readonly string _projectKind;
    private readonly string _validationLabel;
    private readonly bool   _isolateValidation;
    private readonly string _progressNote;
    private readonly string _fixDescriptor;
    private readonly int    _maxFixAttempts;
    private readonly string _coAuthor;

    public string Name { get; }
    public string? Summary { get; }

    public ReviewFlow(
        string      name,
        string?     summary,
        IValidator  validator,
        string      projectKind,
        string      validationLabel,
        bool        isolateValidation = false,
        string      progressNote = "",
        string      fixDescriptor = "",
        int         maxFixAttempts = 3,
        string      coAuthor = "Claude Opus 4.7 + Codex gpt-5.5")
    {
        Name               = name;
        Summary            = summary;
        _validator         = validator;
        _projectKind       = projectKind;
        _validationLabel   = validationLabel;
        _isolateValidation = isolateValidation;
        _progressNote      = progressNote;
        _fixDescriptor     = fixDescriptor;
        _maxFixAttempts    = maxFixAttempts;
        _coAuthor          = coAuthor;
    }

    public async Task<FlowResult> RunAsync(FlowContext ctx, FlowArgs args, CancellationToken ct)
    {
        if (await GitOps.IsDirtyAsync(ctx.ProjectDir, ct))
        {
            await ctx.Sink.PhaseFailAsync("abort", "working tree is dirty. Commit or stash first.", ct);
            return new FlowResult(FlowExitReason.AbortedDirtyTree);
        }

        var claude = new ClaudeAgent { Name = "claude", Sink = ctx.Sink };

        // 1. Claude does the work.
        var work = await claude.RunAsync(new AgentRunRequest(ctx.UserPrompt, null, ctx.ProjectDir), ct);
        await ctx.Sink.PhaseOkAsync("claude", $"turn 1 done (session={work.SessionId})", ct);

        // 2. validate + fix loop. Isolated for validators that dirty the
        //    tree on every run (Unity batch-mode regenerates TMP_SDF /
        //    .meta / Library); the scope reverts that noise at exit.
        ValidateAndFixResult validate;
        await using (var iso = _isolateValidation
            ? await IsolationScope.BeginAsync(ctx.ProjectDir, ctx.Sink, ct)
            : null)
        {
            validate = await Loops.ValidateAndFixAsync(
                claude, _validator, work, ctx.ProjectDir, ctx.Sink,
                maxAttempts:   _maxFixAttempts,
                progressNote:  _progressNote,
                fixDescriptor: _fixDescriptor,
                ct:            ct);
        }
        work = validate.LastResult;
        if (!validate.Ok)
            return new FlowResult(FlowExitReason.ValidationFailed);

        // 3. Nothing changed? skip review/commit.
        var diffText = await GitOps.DiffAsync(new GitDiffRequest(ctx.ProjectDir), ct);
        if (string.IsNullOrWhiteSpace(diffText))
        {
            await ctx.Sink.PhaseInfoAsync("done", "Claude made no file changes. Nothing to review or commit.", ct);
            return new FlowResult(FlowExitReason.NoChanges);
        }

        // 4. Codex review.
        var review = await Reviews.AskCodexForVerdictAsync(
            ctx.ProjectDir, ctx.Session.Dir, ctx.UserPrompt,
            projectKind:     _projectKind,
            validationLabel: _validationLabel,
            sink:            ctx.Sink,
            ct:              ct);

        if (review.IsUnclear)
        {
            await ctx.Sink.PhaseFailAsync("abort",
                $"Codex verdict unclear (review was {review.Text.Length} bytes). Refusing to commit.", ct);
            return new FlowResult(FlowExitReason.VerdictUnclear);
        }

        // 5. one revision round.
        if (review.IsRevise)
        {
            await ctx.Sink.PhaseStartAsync("revise", "sending reviewer feedback to Claude...", ct);
            work = await claude.RunAsync(new AgentRunRequest(
                $"Code reviewer feedback — please address:\n\n{review.Text}",
                work.SessionId, ctx.ProjectDir), ct);

            var v2 = await _validator.ValidateAsync(ctx.ProjectDir, ct);
            if (!v2.Ok)
            {
                await ctx.Sink.PhaseFailAsync("abort", $"post-revision validation failed: {v2.Summary}", ct);
                return new FlowResult(FlowExitReason.RevisionBrokeValidation);
            }
        }

        // 6. commit (+ optional push).
        var filesToCommit = await GitOps.ChangedFilesAsync(ctx.ProjectDir, ct);
        if (filesToCommit.Count == 0)
        {
            await ctx.Sink.PhaseInfoAsync("done", "No files ultimately changed.", ct);
            return new FlowResult(FlowExitReason.NoChanges);
        }

        var commitMessage = Reviews.BuildCommitMessage(ctx.UserPrompt, review.Text);
        await ctx.Sink.PhaseStartAsync("commit", $"{filesToCommit.Count} files...", ct);
        await GitOps.CommitAsync(new GitCommitRequest(
            ProjectDir: ctx.ProjectDir,
            Message:    commitMessage,
            Files:      filesToCommit,
            CoAuthor:   _coAuthor), ct);
        await ctx.Sink.PhaseOkAsync("commit", "done.", ct);

        if (ctx.ShouldPush)
        {
            var branch = await GitOps.CurrentBranchAsync(ctx.ProjectDir, ct);
            await ctx.Sink.PhaseStartAsync("push", $"origin {branch}...", ct);
            await GitOps.PushAsync(new GitPushRequest(ctx.ProjectDir, Branch: branch), ct);
            await ctx.Sink.PhaseOkAsync("push", "done.", ct);
        }

        await ctx.Session.WriteArtifactAsync(SessionArtifact.ClaudeRaw, work.RawOutput, ct);
        await ctx.Session.WriteArtifactAsync(SessionArtifact.CodexReview, review.Text, ct);

        await ctx.Sink.PhaseOkAsync("done", $"Shipped. Transcript: {ctx.Session.Dir}", ct);
        return new FlowResult(FlowExitReason.Shipped);
    }
}
