using RemoteAgents.Infrastructure.CommandLine;

namespace RemoteAgents.Tests;

public class RunCommandTests
{
    [Fact]
    public async Task Echo_captures_stdout_and_zero_exit()
    {
        var res = await RunCommand.RunAsync("echo hello-world");

        Assert.Equal(0, res.ExitCode);
        Assert.Contains("hello-world", res.Stdout);
        Assert.False(res.TimedOut);
    }

    [Fact]
    public async Task Nonzero_exit_is_surfaced()
    {
        var res = await RunCommand.RunAsync("exit 7");

        Assert.Equal(7, res.ExitCode);
        Assert.False(res.TimedOut);
    }

    [Fact]
    public async Task Timeout_is_flagged()
    {
        var sleep = OperatingSystem.IsWindows() ? "ping -n 30 127.0.0.1 > nul" : "sleep 30";
        var res = await RunCommand.RunAsync(sleep, new RunCommandOptions(TimeoutMs: 500));

        Assert.True(res.TimedOut);
    }

    [Fact]
    public async Task EnsureOk_throws_on_nonzero_and_passes_through_on_zero()
    {
        var ok = await RunCommand.RunAsync("echo fine");
        Assert.Same(ok, ok.EnsureOk("echo"));

        var bad = await RunCommand.RunAsync("exit 3");
        var ex = Assert.Throws<InvalidOperationException>(() => bad.EnsureOk("step"));
        Assert.Contains("step failed", ex.Message);
    }
}
