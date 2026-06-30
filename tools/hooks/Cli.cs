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
        "  Install a git post-commit hook that calls `abox-hooks commit`.";

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
            "install-git" => InstallGit(args[1..]),
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

        var dispatched = await controller.DispatchPendingAsync(opts.LogPath, opts.CursorPath);
        Console.WriteLine($"abox-hooks: dispatched {dispatched} event(s)");
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
        var dispatched = await controller.DispatchPendingAsync(
            Path.Combine(hooksDir, "hooks.jsonl"), Path.Combine(hooksDir, "hooks.cursor"));

        var shortSha = head.Sha[..Math.Min(7, head.Sha.Length)];
        Console.WriteLine($"abox-hooks: CommitLanded {shortSha} → dispatched {dispatched} event(s)");
        return 0;
    }

    private static int InstallGit(string[] args)
    {
        var result = GitInstaller.InstallPostCommit(RepoArg(args));
        Console.WriteLine($"abox-hooks: {result.Message}");
        return result.Installed ? 0 : 1;
    }

    private static HookEvent CommitEvent(string repo, GitHead head)
    {
        var raw = new JsonObject { ["sha"] = head.Sha, ["branch"] = head.Branch, ["subject"] = head.Subject };
        using var doc = JsonDocument.Parse(raw.ToJsonString());
        return new HookEvent(HookKind.CommitLanded, HookSource.Git, head.Sha, repo, doc.RootElement.Clone());
    }

    private static string RepoArg(string[] args)
    {
        for (var i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--repo")
                return args[i + 1];
        return Directory.GetCurrentDirectory();
    }

    private static int Fail(string why)
    {
        Console.Error.WriteLine($"abox-hooks: {why}");
        Console.Error.WriteLine(Usage);
        return 1;
    }
}
