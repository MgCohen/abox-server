using System.Text.Json;
using System.Text.Json.Nodes;

namespace ABox.Governance.Hooks;

public static class Cli
{
    private const string Usage =
        "abox-hooks run [--log <path>] [--cursor <path>] [--root <dir>]... [--timeout-ms <n>] [--since-cursor]\n" +
        "  Dispatch pending hook events from the log to the discovered .hook files.\n" +
        "abox-hooks commit [--repo <dir>]\n" +
        "  Emit a CommitLanded event for the repo's HEAD, then dispatch (the post-commit entry point).\n" +
        "abox-hooks install-git [--repo <dir>]\n" +
        "  Install a git post-commit hook that calls `abox-hooks commit`.\n" +
        "abox-hooks turn-ended [--repo <dir>]\n" +
        "  Emit a TurnEnded event from a Claude Code Stop hook (Stop payload on stdin), then dispatch.\n" +
        "abox-hooks install-claude [--repo <dir>] [--settings <path>]\n" +
        "  Install a Claude Code Stop hook that calls `abox-hooks turn-ended`.";

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0) return Fail("no command given");
        if (args[0] is "-h" or "--help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        return args[0] switch
        {
            "run" => await RunDispatch(args[1..]),
            "commit" => await Commit(args[1..]),
            "turn-ended" => await TurnEnded(args[1..]),
            "install-git" => InstallGit(args[1..]),
            "install-claude" => InstallClaude(args[1..]),
            _ => Fail($"unknown command '{args[0]}'"),
        };
    }

    private static async Task<int> RunDispatch(string[] args)
    {
        CliOptions opts;
        try
        {
            opts = CliOptions.Parse(args);
        }
        catch (ArgumentException e)
        {
            return Fail(e.Message);
        }

        var catalog = new HookCatalog(opts.Roots, msg => Console.Error.WriteLine($"abox-hooks: {msg}"));
        var dispatcher = new HookDispatcher(new HookRunner(opts.TimeoutMs));
        var controller = new HookController(catalog, dispatcher);

        var pass = await controller.DispatchPendingAsync(opts.LogPath, opts.CursorPath);
        Console.WriteLine($"abox-hooks: dispatched {pass.Events} event(s)");
        return 0;
    }

    private static async Task<int> Commit(string[] args)
    {
        var repo = RepoArg(args);
        var hooksDir = Path.Combine(repo, ".abox");
        if (!Directory.Exists(hooksDir))
        {
            Console.WriteLine("abox-hooks: no .abox/ — repo has not opted in; nothing emitted.");
            return 0;
        }

        var head = GitHead.Read(repo);
        if (head is null)
        {
            Console.Error.WriteLine("abox-hooks: could not read git HEAD; nothing emitted.");
            return 0;
        }

        HookLog.Append(Path.Combine(hooksDir, "hooks.jsonl"), CommitEvent(repo, head));

        var catalog = new HookCatalog([repo], msg => Console.Error.WriteLine($"abox-hooks: {msg}"));
        var controller = new HookController(catalog, new HookDispatcher(new HookRunner()));
        var pass = await controller.DispatchPendingAsync(
            Path.Combine(hooksDir, "hooks.jsonl"), Path.Combine(hooksDir, "hooks.cursor"));

        var shortSha = head.Sha[..Math.Min(7, head.Sha.Length)];
        Console.WriteLine($"abox-hooks: CommitLanded {shortSha} → dispatched {pass.Events} event(s)");
        return 0;
    }

    private static int InstallGit(string[] args)
    {
        var result = GitInstaller.InstallPostCommit(RepoArg(args));
        Console.WriteLine($"abox-hooks: {result.Message}");
        return result.Installed ? 0 : 1;
    }

    private static async Task<int> TurnEnded(string[] args)
    {
        var repo = ResolveRepo(RepoArg(args));
        var raw = Console.IsInputRedirected ? await Console.In.ReadToEndAsync() : "";
        var outcome = await EmitTurnEndedAsync(repo, raw);
        return ApplyStopFeedback(outcome.Feedback);
    }

    // The testable core: given a repo and the raw Claude Code Stop payload, emit a TurnEnded line
    // (opt-in on .abox/), dispatch, and translate any check-hook output into feedback for the running
    // agent. Returns the dispatched count + feedback, or NotOptedIn (-1) when the repo has not opted in.
    public static async Task<TurnEndedOutcome> EmitTurnEndedAsync(string repo, string rawPayload)
    {
        var hooksDir = Path.Combine(repo, ".abox");
        if (!Directory.Exists(hooksDir)) return TurnEndedOutcome.NotOptedIn;

        HookLog.Append(Path.Combine(hooksDir, "hooks.jsonl"), TurnEndedEvent(repo, rawPayload));

        var catalog = new HookCatalog([repo], msg => Console.Error.WriteLine($"abox-hooks: {msg}"));
        var controller = new HookController(catalog, new HookDispatcher(new HookRunner()));
        var pass = await controller.DispatchPendingAsync(
            Path.Combine(hooksDir, "hooks.jsonl"), Path.Combine(hooksDir, "hooks.cursor"));

        var feedback = HookFeedback.FromChecks(pass.Checks);

        // Loop guard: Claude sets stop_hook_active once a stop has already been blocked and resumed. If a
        // check still wants to block, downgrade it to advisory context so we surface the reason without
        // wedging the agent in an endless block→resume→block cycle on something it cannot satisfy.
        if (feedback.Kind == HookFeedbackKind.Block && StopHookActive(rawPayload))
            feedback = feedback with { Kind = HookFeedbackKind.Context };

        return new TurnEndedOutcome(pass.Events, feedback);
    }

    private static bool StopHookActive(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload)) return false;
        try
        {
            using var doc = JsonDocument.Parse(rawPayload);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("stop_hook_active", out var a)
                && a.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Render the feedback in Claude Code's Stop-hook protocol: exit 2 with the reason on stderr blocks
    // the stop and feeds it back to the agent; exit 0 with hookSpecificOutput.additionalContext on
    // stdout injects advisory context; nothing to say is a silent exit 0. (≤10k chars per the docs.)
    private static int ApplyStopFeedback(HookFeedback fb)
    {
        switch (fb.Kind)
        {
            case HookFeedbackKind.Block:
                Console.Error.WriteLine(Cap(fb.Text));
                return 2;
            case HookFeedbackKind.Context:
                var payload = new JsonObject
                {
                    ["hookSpecificOutput"] = new JsonObject
                    {
                        ["hookEventName"] = "Stop",
                        ["additionalContext"] = Cap(fb.Text),
                    },
                };
                Console.WriteLine(payload.ToJsonString());
                return 0;
            default:
                return 0;
        }
    }

    private static string Cap(string s) => s.Length <= 10_000 ? s : s[..10_000];

    private static int InstallClaude(string[] args)
    {
        var repo = RepoArg(args);
        var settings = OptionArg(args, "--settings") ?? Path.Combine(repo, ".claude", "settings.json");
        var command = $"{SelfInvocation()} turn-ended --repo \"$CLAUDE_PROJECT_DIR\"";

        var result = ClaudeCodeInstaller.InstallStopHook(settings, command);
        Console.WriteLine($"abox-hooks: {result.Message}");
        return result.Installed ? 0 : 1;
    }

    private static HookEvent TurnEndedEvent(string repo, string rawText)
    {
        var sessionId = "";
        JsonElement raw;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawText) ? "{}" : rawText);
            raw = doc.RootElement.Clone();
            if (raw.ValueKind == JsonValueKind.Object
                && raw.TryGetProperty("session_id", out var s) && s.ValueKind == JsonValueKind.String)
                sessionId = s.GetString() ?? "";
        }
        catch (JsonException)
        {
            using var empty = JsonDocument.Parse("{}");
            raw = empty.RootElement.Clone();
        }
        return new HookEvent(HookKind.TurnEnded, HookSource.Claude, sessionId, repo, raw);
    }

    // The installed Stop hook re-invokes THIS executable by absolute path, so it needs nothing
    // on PATH: `dotnet <dll>` for a framework-dependent run, or the apphost exe for a published one.
    private static string SelfInvocation()
    {
        var host = Environment.ProcessPath;
        var entry = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        if (host is not null
            && Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(entry))
            return $"\"{host}\" \"{entry}\"";
        return host is not null ? $"\"{host}\"" : "abox-hooks";
    }

    private static string ResolveRepo(string repo) =>
        !string.IsNullOrEmpty(repo) && Directory.Exists(repo) ? repo : Directory.GetCurrentDirectory();

    private static HookEvent CommitEvent(string repo, GitHead head)
    {
        var raw = new JsonObject { ["sha"] = head.Sha, ["branch"] = head.Branch, ["subject"] = head.Subject };
        using var doc = JsonDocument.Parse(raw.ToJsonString());
        return new HookEvent(HookKind.CommitLanded, HookSource.Git, head.Sha, repo, doc.RootElement.Clone());
    }

    private static string RepoArg(string[] args) => OptionArg(args, "--repo") ?? Directory.GetCurrentDirectory();

    private static string? OptionArg(string[] args, string name)
    {
        for (var i = 0; i + 1 < args.Length; i++)
            if (args[i] == name)
                return args[i + 1];
        return null;
    }

    private static int Fail(string why)
    {
        Console.Error.WriteLine($"abox-hooks: {why}");
        Console.Error.WriteLine(Usage);
        return 1;
    }
}
