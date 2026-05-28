using RemoteAgents.Primitives;

namespace RemoteAgents.Tests.Primitives;

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
    }

    [Fact]
    public async Task Timeout_is_flagged()
    {
        var res = await RunCommand.RunAsync(
            "ping -n 30 127.0.0.1 > nul",
            new RunCommandOptions(TimeoutMs: 500));
        Assert.True(res.TimedOut);
    }
}
