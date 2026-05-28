#:project ../RemoteAgents/RemoteAgents.csproj
#:project ../validation/Validators.csproj
// flows/full-review.cs
//
// The complete pipeline:
//   Claude does work
//   -> project validator (fix loop, up to N attempts)
//   -> Codex reviews the diff
//   -> if Codex says REVISE, one Claude revision pass + re-validate
//   -> commit (push is NOT automatic; pass --push to enable)
//
// Every step here is plain C# you can edit. The library imposes no
// control flow.
//
// Usage:
//   dotnet run flows/full-review.cs <project> "<prompt>" [--push]
//
// Parity reference: remote-agents/orchestrator/flows/full-review.mjs

using RemoteAgents.Agents;
using RemoteAgents.Events;
using RemoteAgents.Primitives;
using RemoteAgents.Sessions;
using RemoteAgents.Validation;
using RemoteAgents.Validation.Orchestrator;

const string FLOW_NAME = "full-review";
const int MAX_FIX_ATTEMPTS = 3;
const int MAX_REVISION_ROUNDS = 1;

var argv = args.ToList();
var pushIdx = argv.IndexOf("--push");
var shouldPush = pushIdx >= 0;
if (shouldPush) argv.RemoveAt(pushIdx);

if (argv.Count < 2)
{
    Console.Error.WriteLine($"Usage: dotnet run flows/{FLOW_NAME}.cs <project> \"<prompt>\" [--push]");
    Environment.ExitCode = 2;
    return;
}

var projectName = argv[0];
var userPrompt = string.Join(' ', argv.Skip(1)).Trim();

await SubscriptionGuard.CheckAsync();

var projectDir = ProjectRegistry.Resolve(projectName);
var session = Session.Start(new StartSessionRequest(
    ProjectDir: projectDir,
    ProjectName: projectName,
    UserPrompt: userPrompt,
    FlowName: FLOW_NAME));

Console.WriteLine($"[{session.Id}]");
Console.WriteLine($"  flow:    {FLOW_NAME}");
Console.WriteLine($"  project: {projectName} ({projectDir})");
Console.WriteLine($"  prompt:  {userPrompt}");
Console.WriteLine($"  push:    {(shouldPush ? "yes" : "no")}");
Console.WriteLine();

// Safety: refuse to run on a dirty tree so we don't mix changes.
if (await GitOps.IsDirtyAsync(projectDir))
{
    Console.Error.WriteLine("[abort] working tree is dirty. Commit or stash first.");
    session.End("aborted-dirty-tree");
    Environment.ExitCode = 2;
    return;
}

var sink = new CompositeSink(
    new ConsoleSink(),
    new JsonlSink(session.TranscriptFile),
    new ProviderJsonlIngestSink(session.Dir, projectDir));

IValidator validator = new OrchestratorValidator();

try
{
    var before = FsDiff.Snapshot(projectDir);

    var claude = new ClaudeAgent { Name = "claude", Sink = sink };
    var codex  = new CodexAgent  { Name = "codex",  Sink = sink };

    // ── 1. Claude does the work ───────────────────────────────────────
    var claudeResult = await claude.RunAsync(new AgentRunRequest(userPrompt, null, projectDir));
    Console.WriteLine($"[claude] turn 1 done (session={claudeResult.SessionId})\n");

    // ── 2. validate + fix loop ────────────────────────────────────────
    bool validationOk = false;
    int validateAttempt = 0;
    ValidationResult v = new(false, "", "");

    while (validateAttempt < MAX_FIX_ATTEMPTS)
    {
        validateAttempt++;
        Console.WriteLine($"[validate] attempt {validateAttempt}...");
        v = await validator.ValidateAsync(projectDir);
        if (v.Ok) { validationOk = true; Console.WriteLine($"[validate] PASSED — {v.Summary}\n"); break; }
        Console.WriteLine($"[validate] FAILED — {v.Summary}");
        if (validateAttempt >= MAX_FIX_ATTEMPTS) break;

        var fixPrompt = $"Validation failed. Address these issues:\n\n{v.Errors}";
        claudeResult = await claude.RunAsync(new AgentRunRequest(fixPrompt, claudeResult.SessionId, projectDir));
        Console.WriteLine($"[claude] fix turn {validateAttempt + 1} done\n");
    }

    if (!validationOk)
    {
        Console.Error.WriteLine($"[abort] validation never passed after {validateAttempt} attempts.");
        session.End("validation-failed");
        Environment.ExitCode = 2;
        return;
    }

    // ── 3. Codex review ───────────────────────────────────────────────
    var diffText = await GitOps.DiffAsync(new GitDiffRequest(projectDir));
    if (string.IsNullOrWhiteSpace(diffText))
    {
        Console.WriteLine("[done] Claude made no file changes. Nothing to review or commit.");
        session.End("no-changes");
        return;
    }

    var reviewPrompt = string.Join("\n", new[]
    {
        "You are reviewing changes made by another agent.",
        "",
        "Original task:",
        userPrompt,
        "",
        "Diff:",
        "```diff",
        diffText,
        "```",
        "",
        "Validation: all project checks passed.",
        "",
        "Reply with EXACTLY one of:",
        "  APPROVE: <one-sentence reason>  — if the work is acceptable to ship.",
        "  REVISE: <issues>                — if it needs another pass.",
        "",
        "Be strict but not pedantic. Don't ask for cosmetic changes.",
    });

    Console.WriteLine($"[codex] reviewing diff ({diffText.Length} bytes)...");
    var codexOptionsReview = new CodexAgentOptions(Sandbox: "read-only", JsonStreamTimeoutMs: 5 * 60_000);
    var review = await new CodexAgent { Name = "codex", Sink = sink, Options = codexOptionsReview }
        .RunAsync(new AgentRunRequest(reviewPrompt, null, projectDir));

    var verdict = review.Text.TrimStart().StartsWith("APPROVE:", StringComparison.OrdinalIgnoreCase) ? "approve"
                : review.Text.TrimStart().StartsWith("REVISE:",  StringComparison.OrdinalIgnoreCase) ? "revise"
                : "unclear";

    Console.WriteLine("[codex] review:");
    foreach (var line in review.Text.Trim().Split('\n')) Console.WriteLine("  " + line);
    Console.WriteLine();

    // Step-11 acceptance: drop the verdict alongside transcript.jsonl
    var textEscaped = review.Text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    await File.WriteAllTextAsync(Path.Combine(session.Dir, "codex-review.jsonl"),
        $"{{\"verdict\":\"{verdict}\",\"sessionId\":\"{review.SessionId}\",\"text\":\"{textEscaped}\"}}\n");

    // ── 4. revision round ─────────────────────────────────────────────
    int revisionRounds = 0;
    while (revisionRounds < MAX_REVISION_ROUNDS && verdict == "revise")
    {
        revisionRounds++;
        Console.WriteLine($"[revise] sending reviewer feedback to Claude (round {revisionRounds})...");
        claudeResult = await claude.RunAsync(new AgentRunRequest(
            $"Code reviewer feedback — please address:\n\n{review.Text}",
            claudeResult.SessionId,
            projectDir));

        var v2 = await validator.ValidateAsync(projectDir);
        if (!v2.Ok)
        {
            Console.Error.WriteLine($"[abort] post-revision validation failed: {v2.Summary}");
            session.End("revision-broke-validation");
            Environment.ExitCode = 2;
            return;
        }
        break; // one revision pass; don't loop
    }

    // ── 5. commit (+ optional push) ───────────────────────────────────
    var after = FsDiff.Snapshot(projectDir);
    var fileDiff = FsDiff.Diff(before, after);

    if (fileDiff.All.Count == 0)
    {
        Console.WriteLine("[done] No files ultimately changed.");
        session.End("no-changes");
        return;
    }

    var reviewLine = review.Text.Split('\n')[0];
    reviewLine = System.Text.RegularExpressions.Regex.Replace(reviewLine, "^(APPROVE|REVISE):\\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

    var commitMessage = string.Join("\n", new[]
    {
        Truncate(userPrompt, 70),
        "",
        userPrompt,
        "",
        $"Reviewed by Codex: {(string.IsNullOrEmpty(reviewLine) ? "(no comment)" : reviewLine)}",
    });

    Console.WriteLine($"[commit] {fileDiff.All.Count} files...");
    await GitOps.CommitAsync(new GitCommitRequest(
        ProjectDir: projectDir,
        Message: commitMessage,
        Files: fileDiff.All,
        CoAuthor: "Claude Opus 4.7 + Codex gpt-5.5"));
    Console.WriteLine("[commit] done.");

    if (shouldPush)
    {
        var branch = await GitOps.CurrentBranchAsync(projectDir);
        Console.WriteLine($"[push] origin {branch}...");
        await GitOps.PushAsync(new GitPushRequest(projectDir, Branch: branch));
        Console.WriteLine("[push] done.");
    }

    await File.WriteAllTextAsync(Path.Combine(session.Dir, "claude-raw.txt"), claudeResult.RawOutput);
    await File.WriteAllTextAsync(Path.Combine(session.Dir, "codex-review.txt"), review.Text);

    session.End("shipped");

    Console.WriteLine();
    Console.WriteLine("──────────────────────────────────────────");
    Console.WriteLine($"Shipped. Transcript: {session.Dir}");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[{FLOW_NAME}] FAILED: {ex}");
    session.End("failed", failureReason: ex.Message);
    Environment.ExitCode = 1;
}

static string Truncate(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
