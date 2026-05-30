using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using RemoteAgents.Events;
using RemoteAgents.Primitives;
using RemoteAgents.Runs;

namespace RemoteAgents.Agents;

// Drives `codex exec` non-interactively. codex exec is officially supported
// on ChatGPT subscriptions (since April 2026), so no PTY trick needed — a
// plain Process is fine. Captures the agent's final message via `-o
// <tmpfile>` and scans --json stdout for the session id.
//
// Non-sealed (Q7) for the same reason as ClaudeAgent — concrete projects
// may want to swap in a different model or different sandbox policy.
public class CodexAgent : Agent
{
    public CodexAgent() : base("codex") { }

    public CodexAgentOptions Options { get; init; } = new();
    protected override AgentOptions BaseOptions => Options;

    public static List<string> BuildCodexArgs(string? sessionId, string projectDir, string lastMessageFile, CodexAgentOptions opts)
    {
        var args = new List<string>();
        if (sessionId is not null) { args.Add("exec"); args.Add("resume"); args.Add(sessionId); }
        else                        { args.Add("exec"); }

        args.Add("--cd"); args.Add(projectDir);
        args.Add("-o"); args.Add(lastMessageFile);
        args.Add("--sandbox"); args.Add(opts.Sandbox);
        // `codex exec` is autonomous by default — no approval-prompt
        // flag exists. The old `--dangerously-bypass-approvals-and-sandbox`
        // also disabled hook invocation (smoke confirmed hooks.jsonl
        // stayed empty under that mode); dropping it without replacement
        // gives us hooks-on autonomy.
        //
        // `--dangerously-bypass-hook-trust` skips the per-hook trust gate
        // for this invocation, so a freshly-installed ~/.codex/hooks.json
        // runs without an out-of-band trust prompt.
        args.Add("--dangerously-bypass-hook-trust");
        // The orchestrator explicitly knows the project dir; codex's
        // default trust-check refuses to run in non-git dirs (or
        // first-time-seen ones). The old --dangerously-bypass-approvals
        // flag silently covered this; now we're explicit.
        args.Add("--skip-git-repo-check");
        args.Add("--json");
        if (!string.IsNullOrEmpty(opts.Model)) { args.Add("--model"); args.Add(opts.Model); }

        // `-` tells codex to read the prompt from stdin.
        args.Add("-");
        return args;
    }

    // Hook wiring consumed by the Agent base. Resolution + violation
    // emission live in the base; this getter declares what to install,
    // how to tear it down, and how to parse the resulting hooks.jsonl.
    // Codex hooks are user-global (~/.codex/hooks.json) — ignores
    // req.ProjectDir and resolves the config dir from the user profile.
    protected override HookIntegration? Hooks => Options.Hooks is null ? null
        : new HookIntegration(
            HooksJsonlPath: Options.Hooks.HooksJsonlPath,
            Parser:         new CodexHookParser(),
            Install:        _ =>
            {
                var configDir = CodexHookConfig.DefaultConfigDir();
                CodexHookConfig.Install(configDir, Options.Hooks.ShimPath);
                return () => CodexHookConfig.Uninstall(configDir);
            });

    protected override async Task<DriveResult> DriveAsync(AgentDriveContext ctx, CancellationToken ct)
    {
        var req = ctx.Request;
        var tmpDir = Path.Combine(Path.GetTempPath(), "agents-codex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var lastMessageFile = Path.Combine(tmpDir, "last.txt");

        var args = BuildCodexArgs(req.SessionId, req.ProjectDir, lastMessageFile, Options);
        var commandLine = "codex " + string.Join(' ', args.Select(Shell.QuoteArg));

        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/c {commandLine}",
            WorkingDirectory = req.ProjectDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Defense in depth: blank the API-key vars (centralized in EnvScrub)
        // on the child env so codex can't accidentally fall through to API
        // billing if SubscriptionGuard ever regresses.
        foreach (var k in EnvScrub.SubscriptionKeys) psi.Environment[k] = "";
        if (ctx.HooksJsonlPath is not null)
            psi.Environment["REMOTEAGENTS_HOOKS_JSONL"] = ctx.HooksJsonlPath;

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        string? extractedSessionId = req.SessionId;

        // Pump stream chunks through an unbounded channel so EmitAsync is
        // awaited in order on a single consumer task. The previous
        // `_ = Sink.EmitAsync(...)` would race with itself under chatty
        // output and silently swallow sink exceptions.
        var chunks = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdout.AppendLine(e.Data);
            if (extractedSessionId is null)
            {
                var found = CodexSessionId.Scan(e.Data);
                if (found is not null)
                {
                    extractedSessionId = found;
                    // Fire-and-forget — the channel consumer (pump) is what
                    // serialises sink writes for chunks; provider-session
                    // is a one-shot announcement, ordering vs StreamChunks
                    // is acceptable best-effort.
                    _ = Sink.EmitAsync(new AgentEvent.ProviderSessionAttached(
                        DateTimeOffset.UtcNow, Name, new ProviderSessionRef("codex", found)));
                }
            }
            chunks.Writer.TryWrite(e.Data + "\n");
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) stderr.AppendLine(e.Data);
        };

        var pump = Task.Run(async () =>
        {
            await foreach (var chunk in chunks.Reader.ReadAllAsync())
            {
                await Sink.EmitAsync(new AgentEvent.StreamChunk(DateTimeOffset.UtcNow, Name, chunk));
            }
        });

        // System prompt prepended to user prompt — codex has no flag for it.
        // ctx.SystemPrompt has already had UnattendedDirective.Compose
        // applied by the base.
        var fullPrompt = string.IsNullOrEmpty(ctx.SystemPrompt)
            ? req.Prompt
            : ctx.SystemPrompt + "\n\n" + req.Prompt;

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.StandardInput.WriteAsync(fullPrompt);
        proc.StandardInput.Close();

        using var timeoutCts = new CancellationTokenSource(Options.JsonStreamTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try { await proc.WaitForExitAsync(linkedCts.Token); }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            try { await proc.WaitForExitAsync(CancellationToken.None); } catch { }
            if (!timeoutCts.IsCancellationRequested) throw;
        }

        // Drain any chunks queued between WaitForExit returning and the
        // OutputDataReceived callbacks finishing.
        chunks.Writer.Complete();
        await pump;

        string text = "";
        if (File.Exists(lastMessageFile))
            text = await File.ReadAllTextAsync(lastMessageFile, ct);

        try { Directory.Delete(tmpDir, recursive: true); } catch { /* best-effort */ }

        // Codex exec's `-o` output mirrors what would land in a Stop hook's
        // last_assistant_message. If hooks didn't fire (config-shape issues,
        // version regressions, or an accidentally-restored "skip hooks"
        // flag), the sentinel / heuristic still surface from text — so we
        // append a synthetic codex.stop line and let the base resolve via
        // the single hooks.jsonl path. Safe post-WaitForExitAsync: codex has
        // exited, the shim can no longer fire, no writer race.
        if (ctx.HooksJsonlPath is not null && !string.IsNullOrWhiteSpace(text))
            await AppendSyntheticStopLineAsync(
                ctx.HooksJsonlPath, extractedSessionId, req.ProjectDir, text);

        return new DriveResult(
            Text:      text,
            SessionId: extractedSessionId ?? "",
            ExitCode:  proc.HasExited ? proc.ExitCode : -1,
            RawOutput: stdout.ToString());
    }

    private static async Task AppendSyntheticStopLineAsync(
        string hooksJsonlPath, string? sessionId, string cwd, string lastAssistantMessage)
    {
        var wrapped = new JsonObject
        {
            ["ts"]        = DateTimeOffset.UtcNow.ToString("o"),
            ["source"]    = "codex.stop",
            ["sessionId"] = sessionId ?? "",
            ["cwd"]       = cwd,
            ["payload"]   = new JsonObject
            {
                ["last_assistant_message"] = lastAssistantMessage,
                ["_synthetic"]             = "codex.text",
            },
        };
        await File.AppendAllTextAsync(hooksJsonlPath, wrapped.ToJsonString() + "\n");
    }
}
