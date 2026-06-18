using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

return SpikeRunner.Run();

static class SpikeRunner
{
    static readonly BotIdentity Bot = new("ABox-Agent", "294015314+ABox-Agent@users.noreply.github.com");
    const string SecretValue = "ghp_FAKEdeadbeefSPIKEonly_DO_NOT_USE";

    public static int Run()
    {
        var rows = new List<Row>();
        var pcs = new List<string>();

        var s = Setup();
        var fp = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(s.Secret)))[..12];
        pcs.Add($"PC1 secret exists, harvester CAN read it — sha256:{fp} len={new FileInfo(s.Secret).Length}");

        rows.Add(R1_HookRce(s));
        rows.Add(R2_Symlink(Setup()));
        rows.Add(R3_SymlinkToctou(Setup(), pcs));
        rows.Add(R4_Fifo(Setup()));
        rows.Add(R6_EmptyDiff(Setup()));
        rows.Add(A7_AuthoredAsBot(Setup()));

        return Report(rows, pcs);
    }

    // R1 — a worker-planted .git/hooks/pre-commit must never be ingested or run as
    // the (privileged) harvester on commit. Defense: enumeration prunes every .git
    // segment; commit forces core.hooksPath=/dev/null.
    static Row R1_HookRce(Workspace s)
    {
        var pwned = Path.Combine(s.Root, "PWNED");
        var hookDir = Path.Combine(s.Work, ".git", "hooks");
        Directory.CreateDirectory(hookDir);
        File.WriteAllText(Path.Combine(hookDir, "pre-commit"), $"#!/bin/sh\ntouch \"{pwned}\"\n");
        Shell("chmod", $"+x {Path.Combine(hookDir, "pre-commit")}");
        File.AppendAllText(Path.Combine(s.Work, "hello.txt"), "edit\n");

        Harvest.Run(s.Work, s.CloneDir, s.Bare, Bot);

        var clean = !File.Exists(pwned) && !Directory.Exists(Path.Combine(s.CloneDir, ".git", "hooks", "pre-commit").Replace("hooks", "worker"));
        var hookInClone = File.Exists(Path.Combine(s.CloneDir, ".git", "hooks", "pre-commit"))
            && File.ReadAllText(Path.Combine(s.CloneDir, ".git", "hooks", "pre-commit")).Contains("PWNED");

        // negative control: a naive recursive copy WOULD ingest the hook (proves the hole is real).
        var n = Setup();
        File.AppendAllText(Path.Combine(n.Work, "hello.txt"), "edit\n");
        Directory.CreateDirectory(Path.Combine(n.Work, ".git", "hooks"));
        File.WriteAllText(Path.Combine(n.Work, ".git", "hooks", "pre-commit"), "#!/bin/sh\ntouch /tmp/abox-naive-pwn\n");
        NaiveCopyAll(n.Work, n.CloneDir);
        var naiveIngestedGit = Directory.Exists(Path.Combine(n.CloneDir, ".git", "hooks"));

        var pass = clean && !hookInClone && naiveIngestedGit;
        return new Row("R1", "worker .git/hooks executes/ingested on harvest", pass,
            pass ? "hook not ingested, never ran (naive copy DID ingest it)" : "hook reached the clone or ran");
    }

    // R2 — a worker symlink must not reach the committed tree.
    static Row R2_Symlink(Workspace s)
    {
        File.AppendAllText(Path.Combine(s.Work, "hello.txt"), "edit\n");
        Shell("ln", $"-s {s.Secret} {Path.Combine(s.Work, "leaked")}");
        Harvest.Run(s.Work, s.CloneDir, s.Bare, Bot);
        var links = EnumerateNonGit(s.CloneDir).Any(IsSymlink);
        return new Row("R2", "worker symlink ingested into the commit", !links,
            links ? "symlink in committed tree" : "no symlink in committed tree");
    }

    // R3 — a path that is a symlink to the secret must not have its TARGET copied.
    // Defense: lstat-classify and skip non-regular files; never follow the link.
    static Row R3_SymlinkToctou(Workspace s, List<string> pcs)
    {
        Shell("ln", $"-s {s.Secret} {Path.Combine(s.Work, "race.txt")}");
        File.AppendAllText(Path.Combine(s.Work, "hello.txt"), "edit\n");
        Harvest.Run(s.Work, s.CloneDir, s.Bare, Bot);
        var leakedSafe = TreeContains(s.CloneDir, SecretValue);

        // negative control: a naive copy follows the symlink and copies the secret.
        var n = Setup();
        Shell("ln", $"-s {n.Secret} {Path.Combine(n.Work, "race.txt")}");
        NaiveCopyAll(n.Work, n.CloneDir);
        var leakedNaive = TreeContains(n.CloneDir, SecretValue);
        pcs.Add($"PC2 R3 detector live — naive copy {(leakedNaive ? "DID" : "did NOT")} leak the secret (negative control)");

        var pass = !leakedSafe && leakedNaive;
        return new Row("R3", "harvest follows a symlink to the secret", pass,
            pass ? "link not followed; no secret copied" : "secret content reached the clone");
    }

    // R4 — a FIFO in the tree must not hang the harvest. Defense: lstat skips any
    // non-regular file, so it is never open()ed.
    static Row R4_Fifo(Workspace s)
    {
        File.AppendAllText(Path.Combine(s.Work, "hello.txt"), "edit\n");
        Shell("mkfifo", Path.Combine(s.Work, "race.txt"));
        var sw = Stopwatch.StartNew();
        var done = Task.Run(() => Harvest.Run(s.Work, s.CloneDir, s.Bare, Bot)).Wait(TimeSpan.FromSeconds(8));
        sw.Stop();
        var fifoSkipped = !File.Exists(Path.Combine(s.CloneDir, "race.txt"));
        var pass = done && fifoSkipped;
        return new Row("R4", "FIFO in the tree blocks the harvest", pass,
            pass ? $"non-regular skipped, no hang ({sw.ElapsedMilliseconds} ms)" : "harvest hung or copied the FIFO");
    }

    // R6 — an empty diff must not throw (which would strand teardown). Defense: the
    // commit step is empty-diff tolerant.
    static Row R6_EmptyDiff(Workspace s)
    {
        bool threw = false;
        try { Harvest.Run(s.Work, s.CloneDir, s.Bare, Bot); }
        catch { threw = true; }
        return new Row("R6", "empty-diff harvest aborts the run", !threw,
            threw ? "empty diff threw (would strand teardown)" : "empty diff tolerated, harvest returned");
    }

    // A7 — the harvested diff lands on the remote authored by the bot identity.
    static Row A7_AuthoredAsBot(Workspace s)
    {
        File.AppendAllText(Path.Combine(s.Work, "hello.txt"), "agent was here\n");
        Harvest.Run(s.Work, s.CloneDir, s.Bare, Bot);
        var author = GitLogAuthor(s.Bare).Trim();
        var want = $"{Bot.Name} <{Bot.Email}>";
        var pass = author == want;
        return new Row("A7", "control plane commits agent diff as the bot", pass,
            pass ? $"landed authored by {author}" : $"unexpected author {author}");
    }

    static Workspace Setup()
    {
        var root = Path.Combine(Path.GetTempPath(), "abox-dotnet-spike", Guid.NewGuid().ToString("N")[..8]);
        var bare = Path.Combine(root, "remote.git");
        var clone = Path.Combine(root, "clone");
        var work = Path.Combine(root, "work");
        var secret = Path.Combine(root, "secret.token");
        Directory.CreateDirectory(root);
        File.WriteAllText(secret, SecretValue + "\n");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(secret, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        Shell("git", $"init -q --bare {bare}");
        Shell("git", $"-C {bare} symbolic-ref HEAD refs/heads/main");
        Shell("git", $"clone -q {bare} {clone}");
        Shell("git", $"-C {clone} config core.hooksPath /dev/null");
        File.WriteAllText(Path.Combine(clone, "hello.txt"), "hello from base\n");
        GitAs(clone, Bot, "add hello.txt");
        GitAs(clone, Bot, "commit -q -m base");
        Shell("git", $"-C {clone} push -q origin HEAD:refs/heads/main");

        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "hello.txt"), "hello from base\n");
        return new Workspace(root, bare, clone, work, secret);
    }

    static int Report(List<Row> rows, List<string> pcs)
    {
        var sb = new StringBuilder();
        sb.AppendLine().AppendLine("  positive controls (the targets are real)");
        foreach (var p in pcs) sb.AppendLine("  " + p);
        sb.AppendLine().AppendLine("  .NET harvest acceptance matrix (return-path rows)");
        sb.AppendLine($"  {"ID",-3} {"REQUIRED",-9} {"ACTUAL",-9} {"OK",-3} ATTACK");
        sb.AppendLine("  " + new string('-', 67));
        var allPass = true;
        foreach (var r in rows)
        {
            allPass &= r.Pass;
            sb.AppendLine($"  {r.Id,-3} {"PASS",-9} {(r.Pass ? "PASS" : "FAIL"),-9} {(r.Pass ? "OK" : "XX"),-3} {r.Desc}");
        }
        sb.AppendLine("  " + new string('-', 67));
        sb.AppendLine(allPass
            ? "  RESULT: .NET harvest GREEN — every return-path row met its required result."
            : "  RESULT: .NET harvest has rows off their required result (see XX above).");
        foreach (var r in rows.Where(r => !r.Pass)) sb.AppendLine($"  ! {r.Id}: {r.Detail}");
        Console.WriteLine(sb.ToString());
        return allPass ? 0 : 1;
    }

    static IEnumerable<string> EnumerateNonGit(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Contains(".git"));

    static bool IsSymlink(string p) => new FileInfo(p).LinkTarget is not null || (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0;

    static bool TreeContains(string root, string needle) =>
        EnumerateNonGit(root).Where(File.Exists).Any(f =>
        {
            try { return File.ReadAllText(f).Contains(needle); } catch { return false; }
        });

    static void NaiveCopyAll(string src, string dst)
    {
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(src, dst));
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var target = f.Replace(src, dst);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target, true);
        }
    }

    static void GitAs(string repo, BotIdentity bot, string args) =>
        Shell("git", $"-C {repo} -c user.name={bot.Name} -c user.email={bot.Email} -c core.hooksPath=/dev/null {args}");

    static void Shell(string file, string args) => ShellOut(file, args);

    static string GitLogAuthor(string bare)
    {
        var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-C", bare, "log", "-1", "main", "--format=%an <%ae>" }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return stdout;
    }

    static string ShellOut(string file, string args)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in SplitArgs(args)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return stdout;
    }

    static IEnumerable<string> SplitArgs(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}

// The return-path harvest primitive, in C#. The seam is DATA, not control: ingest
// regular-file CONTENT only — never the worker's .git (at any depth), hooks, or
// non-regular files (symlink/FIFO/device) — then commit with hooks off, tolerating
// an empty diff. These behaviors are all NON-DEFAULT; this is the executable
// acceptance criteria the real .NET/ConPTY harvest must keep passing.
static class Harvest
{
    public static void Run(string work, string clone, string bare, BotIdentity bot)
    {
        Ingest(work, clone);
        StripSymlinks(clone);
        CommitPush(clone, bare, bot);
    }

    static void Ingest(string work, string clone)
    {
        foreach (var src in Directory.EnumerateFileSystemEntries(work, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(work, src);
            if (rel.Split(Path.DirectorySeparatorChar).Contains(".git")) continue;
            if (!IsRegularFile(src)) continue;
            var dst = Path.Combine(clone, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, true);
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(dst, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
    }

    static void StripSymlinks(string clone)
    {
        foreach (var p in Directory.EnumerateFileSystemEntries(clone, "*", SearchOption.AllDirectories))
        {
            if (p.Split(Path.DirectorySeparatorChar).Contains(".git")) continue;
            if (new FileInfo(p).LinkTarget is not null) File.Delete(p);
        }
    }

    static void CommitPush(string clone, string bare, BotIdentity bot)
    {
        Git(clone, bot, "add -A");
        if (Run(clone, "git diff --cached --quiet")) return;
        Git(clone, bot, "commit -q -m apply-worker-diff");
        Git(clone, bot, $"push -q origin HEAD:refs/heads/main");
    }

    // lstat (never follows the link) classifies the path; only S_IFREG is ingested,
    // which excludes symlinks, FIFOs, sockets, and devices in one check.
    static bool IsRegularFile(string path)
    {
        if (Libc.lstat(path, out var st) != 0) return false;
        return (st.st_mode & Libc.S_IFMT) == Libc.S_IFREG;
    }

    static void Git(string repo, BotIdentity bot, string args)
    {
        var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-C"); psi.ArgumentList.Add(repo);
        psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("core.hooksPath=/dev/null");
        foreach (var a in args.Split(' ', StringSplitOptions.RemoveEmptyEntries)) psi.ArgumentList.Add(a);
        psi.Environment["GIT_AUTHOR_NAME"] = bot.Name; psi.Environment["GIT_AUTHOR_EMAIL"] = bot.Email;
        psi.Environment["GIT_COMMITTER_NAME"] = bot.Name; psi.Environment["GIT_COMMITTER_EMAIL"] = bot.Email;
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
        p.WaitForExit();
    }

    static bool Run(string repo, string cmd)
    {
        var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var psi = new ProcessStartInfo(parts[0]) { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-C"); psi.ArgumentList.Add(repo);
        foreach (var a in parts.Skip(1)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0;
    }
}

record BotIdentity(string Name, string Email);

record Row(string Id, string Desc, bool Pass, string Detail);

record Workspace(string Root, string Bare, string CloneDir, string Work, string Secret);

static class Libc
{
    public const uint S_IFMT = 0xF000;
    public const uint S_IFREG = 0x8000;

    [StructLayout(LayoutKind.Explicit, Size = 144)]
    public struct Stat
    {
        [FieldOffset(24)] public uint st_mode;
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "lstat")]
    public static extern int lstat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out Stat buf);
}
