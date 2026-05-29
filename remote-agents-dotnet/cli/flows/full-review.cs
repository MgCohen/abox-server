#:project ../../src/RemoteAgents/RemoteAgents.csproj
// flows/full-review.cs
//
// Claude works → orchestrator-validator (Roslyn parse) fix loop → Codex
// reviews diff → optional revision pass → commit (push opt-in).
//
// Usage:
//   dotnet run flows/full-review.cs <project> "<prompt>" [--push]

using RemoteAgents.Agents;
using RemoteAgents.Events;
using RemoteAgents.Flows;
using RemoteAgents.Primitives;
using RemoteAgents.Validation.Orchestrator;

const string FLOW_NAME = "full-review";
const int MAX_FIX_ATTEMPTS = 3;
const string CO_AUTHOR = "Claude Opus 4.7 + Codex gpt-5.5";

await using var ctx = await FlowBootstrap.StartAsync(args, FLOW_NAME);
if (ctx is null) return;
if (!await ctx.EnsureCleanTreeAsync()) return;

try
{
    var claude = new ClaudeAgent { Name = "claude", Sink = ctx.Sink };
    var validator = new OrchestratorValidator();

    // 1. Claude does the work
    var work = await claude.RunAsync(new AgentRunRequest(ctx.UserPrompt, null, ctx.ProjectDir));
    await ctx.Sink.PhaseOkAsync("claude", $"turn 1 done (session={work.SessionId})");

    // 2. validate + fix loop
    var validate = await Loops.ValidateAndFixAsync(
        claude, validator, work, ctx.ProjectDir, ctx.Sink, maxAttempts: MAX_FIX_ATTEMPTS);
    work = validate.LastResult;
    if (!validate.Ok)
    {
        ctx.Session.End("validation-failed");
        Environment.ExitCode = 2;
        return;
    }

    // 3. Nothing changed? skip review/commit.
    var diffText = await GitOps.DiffAsync(new GitDiffRequest(ctx.ProjectDir));
    if (string.IsNullOrWhiteSpace(diffText))
    {
        await ctx.Sink.PhaseInfoAsync("done", "Claude made no file changes. Nothing to review or commit.");
        ctx.Session.End("no-changes");
        Environment.ExitCode = 0;
        return;
    }

    // 4. Codex review
    var review = await Reviews.AskCodexForVerdictAsync(
        ctx.ProjectDir, ctx.Session.Dir, ctx.UserPrompt,
        projectKind: "changes",
        validationLabel: "all project checks passed",
        sink: ctx.Sink);

    if (review.IsUnclear)
    {
        await ctx.Sink.PhaseFailAsync("abort",
            $"Codex verdict unclear (review was {review.Text.Length} bytes). Refusing to commit.");
        ctx.Session.End("verdict-unclear");
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
            ctx.Session.End("revision-broke-validation");
            Environment.ExitCode = 2;
            return;
        }
    }

    // 6. commit (+ optional push)
    var filesToCommit = await GitOps.ChangedFilesAsync(ctx.ProjectDir);
    if (filesToCommit.Count == 0)
    {
        await ctx.Sink.PhaseInfoAsync("done", "No files ultimately changed.");
        ctx.Session.End("no-changes");
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

    await File.WriteAllTextAsync(Path.Combine(ctx.Session.Dir, "claude-raw.txt"), work.RawOutput);
    await File.WriteAllTextAsync(Path.Combine(ctx.Session.Dir, "codex-review.txt"), review.Text);

    ctx.Session.End("shipped");
    await ctx.Sink.PhaseOkAsync("done", $"Shipped. Transcript: {ctx.Session.Dir}");
    Environment.ExitCode = 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[{FLOW_NAME}] FAILED: {ex}");
    ctx.Session.End("failed", failureReason: ex.Message);
    Environment.ExitCode = 1;
}
