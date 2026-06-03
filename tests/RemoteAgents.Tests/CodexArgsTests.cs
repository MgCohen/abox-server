using RemoteAgents.Actors.Agents.Codex;

namespace RemoteAgents.Tests;

public class CodexArgsTests
{
    [Fact]
    public void Fresh_run_starts_with_exec_and_reads_stdin()
    {
        var args = CodexArgs.Build(null, "C:/proj", "C:/tmp/last.txt", "gpt-5.5", "read-only");

        Assert.Equal("exec", args[0]);
        Assert.DoesNotContain("resume", args);
        Assert.Equal("-", args[^1]);
    }

    [Fact]
    public void Resumed_run_threads_the_session_id()
    {
        var args = CodexArgs.Build("sess-12345678", "C:/proj", "C:/tmp/last.txt", "gpt-5.5", "read-only");

        Assert.Equal(new[] { "exec", "resume", "sess-12345678" }, args.Take(3));
    }

    [Fact]
    public void Carries_dir_output_sandbox_model_and_json()
    {
        var args = CodexArgs.Build(null, "C:/proj", "C:/tmp/last.txt", "gpt-5.5", "read-only");

        AssertPair(args, "--cd", "C:/proj");
        AssertPair(args, "-o", "C:/tmp/last.txt");
        AssertPair(args, "--sandbox", "read-only");
        AssertPair(args, "--model", "gpt-5.5");
        Assert.Contains("--json", args);
        Assert.Contains("--skip-git-repo-check", args);
    }

    [Fact]
    public void Omits_model_flag_when_model_is_blank()
    {
        var args = CodexArgs.Build(null, "C:/proj", "C:/tmp/last.txt", "", "read-only");

        Assert.DoesNotContain("--model", args);
    }

    private static void AssertPair(List<string> args, string flag, string value)
    {
        var i = args.IndexOf(flag);
        Assert.True(i >= 0 && i + 1 < args.Count, $"missing flag {flag}");
        Assert.Equal(value, args[i + 1]);
    }
}
