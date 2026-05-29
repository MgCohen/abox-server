#:project ../../src/RemoteAgents/RemoteAgents.csproj
// flows/unity-review.cs
//
// Claude works → UnityBatchValidator (Unity batch-mode compile) fix loop →
// Codex reviews diff → optional revision pass → commit (push opt-in).
//
// Unity batch-mode regenerates TMP_SDF / .meta / Library on every run, so
// the validate/fix loop runs inside an IsolationScope that snapshots
// Claude-touched files BEFORE validating and restores everything else
// AFTER.
//
// Usage:
//   dotnet run flows/unity-review.cs <project> "<prompt>" [--push]

using RemoteAgents.Agents;
using RemoteAgents.Events;
using RemoteAgents.Flows;
using RemoteAgents.Primitives;
using RemoteAgents.Sessions;
using RemoteAgents.Validation.Unity;

const string FLOW_NAME = "unity-review";
const int MAX_FIX_ATTEMPTS = 3;
const string CO_AUTHOR = "Claude Opus 4.7 + Codex gpt-5.5";

await using var ctx = await FlowBootstrap.StartAsync(args, FLOW_NAME);
if (ctx is null) return;
if (!await ctx.EnsureCleanTreeAsync()) return;

try
{
    var claude = new ClaudeAgent { Name = "claude", Sink = ctx.Sink };
    var validator = new UnityFullValidator();

    // 1. Claude does the work
    var work = await claude.RunAsync(new AgentRunRequest(ctx.UserPrompt, null, ctx.ProjectDir));
    await ctx.Sink.PhaseOkAsync("claude", $"turn 1 done (session={work.SessionId})");

    // 2. validate + fix loop, isolated so Unity's regenerated files
    //    (TMP_SDF / .meta / Library) get cleaned up at scope exit.
    ValidateAndFixResult validate;
    await using (var iso = await IsolationScope.BeginAsync(ctx.ProjectDir, ctx.Sink))
    {
        validate = await Loops.ValidateAndFixAsync(
            claude, validator, work, ctx.ProjectDir, ctx.Sink,
            maxAttempts: MAX_FIX_ATTEMPTS,
            progressNote: " (Unity batch-mode, this can take minutes)",
            fixDescriptor: "Unity batch-mode compile");
    }
    work = validate.LastResult;
    if (!validate.Ok)
    {
        ctx.Session.End(SessionResult.ValidationFailed);
        Environment.ExitCode = 2;
        return;
    }

    // 3. Nothing changed? skip review/commit.
    var diffText = await GitOps.DiffAsync(new GitDiffRequest(ctx.ProjectDir));
    if (string.IsNullOrWhiteSpace(diffText))
    {
        await ctx.Sink.PhaseInfoAsync("done", "Claude made no file changes. Nothing to review or commit.");
        ctx.Session.End(SessionResult.NoChanges);
        Environment.ExitCode = 0;
        return;
    }

    // 4. Codex review
    var review = await Reviews.AskCodexForVerdictAsync(
        ctx.ProjectDir, ctx.Session.Dir, ctx.UserPrompt,
        projectKind: "a Unity C# change",
        validationLabel: "Unity batch-mode compile passed",
        sink: ctx.Sink);

    if (review.IsUnclear)
    {
        await ctx.Sink.PhaseFailAsync("abort",
            $"Codex verdict unclear (review was {review.Text.Length} bytes). Refusing to commit.");
        ctx.Session.End(SessionResult.VerdictUnclear);
        Environment.ExitCode = 2;
        return;
    }

    // 5. one revision round
    if (review.IsRevise)
    {
        await ctx.Sink.PhaseStartAsync("revise", "sending reviewer feedback to Claude...");
        work = await claude.RunAsync(new AgentRunRequest(
            $"Code reviewer feedback — please address:\n\n{review.Text}",
            work.SessionId, ctx.ProjectDir));

        var v2 = await validator.ValidateAsync(ctx.ProjectDir);
        if (!v2.Ok)
        {
            await ctx.Sink.PhaseFailAsync("abort", $"post-revision validation failed: {v2.Summary}");
            ctx.Session.End(SessionResult.RevisionBrokeValidation);
            Environment.ExitCode = 2;
            return;
        }
    }

    // 6. commit (+ optional push)
    var filesToCommit = await GitOps.ChangedFilesAsync(ctx.ProjectDir);
    if (filesToCommit.Count == 0)
    {
        await ctx.Sink.PhaseInfoAsync("done", "No files ultimately changed.");
        ctx.Session.End(SessionResult.NoChanges);
        Environment.ExitCode = 0;
        return;
    }

    var commitMessage = Reviews.BuildCommitMessage(ctx.UserPrompt, review.Text);
    await ctx.Sink.PhaseStartAsync("commit", $"{filesToCommit.Count} files...");
    await GitOps.CommitAsync(new GitCommitRequest(
        ProjectDir: ctx.ProjectDir,
        Message: commitMessage,
        Files: filesToCommit,
        CoAuthor: CO_AUTHOR));
    await ctx.Sink.PhaseOkAsync("commit", "done.");

    if (ctx.ShouldPush)
    {
        var branch = await GitOps.CurrentBranchAsync(ctx.ProjectDir);
        await ctx.Sink.PhaseStartAsync("push", $"origin {branch}...");
        await GitOps.PushAsync(new GitPushRequest(ctx.ProjectDir, Branch: branch));
        await ctx.Sink.PhaseOkAsync("push", "done.");
    }

    await ctx.Session.WriteArtifactAsync(SessionArtifact.ClaudeRaw, work.RawOutput);
    await ctx.Session.WriteArtifactAsync(SessionArtifact.CodexReview, review.Text);

    ctx.Session.End(SessionResult.Shipped);
    await ctx.Sink.PhaseOkAsync("done", $"Shipped. Transcript: {ctx.Session.Dir}");
    Environment.ExitCode = 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[{FLOW_NAME}] FAILED: {ex}");
    ctx.Session.End(SessionResult.Failed, failureReason: ex.Message);
    Environment.ExitCode = 1;
}
