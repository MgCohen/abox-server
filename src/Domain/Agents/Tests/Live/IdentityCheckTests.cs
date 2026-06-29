using ABox.Infrastructure.CommandLine;
using ABox.Tests.Harness;
using Xunit.Abstractions;

namespace ABox.Agents.Tests.Live;

// Live validation of the agent/owner credential split: drives the governance
// identity-check script against the real machine and proves a probe commit lands
// as the bot, never the owner or the unconfigured Claude default. Gated by
// [LiveFact] — runs only under RUN_LIVE=1, on the owner's agent-configured
// machine; a scripted environment can't establish which credential git uses.
public class IdentityCheckTests(ITestOutputHelper output)
{
    private static readonly string Script =
        Path.Combine(RepoTree.Root, "governance", "identity-check.sh");

    [Rule("the agent-configured machine → a real commit lands as the bot, never the owner or generic default")]
    [LiveFact]
    public async Task Identity_check_confirms_the_bot_and_no_owner_fallback()
    {
        var result = await RunCommand.RunAsync($"sh {Shell.QuoteArg(Script)}");

        output.WriteLine(result.Stdout);
        output.WriteLine(result.Stderr);

        Assert.True(result.ExitCode == 0, $"identity-check reported a wrong or leaking credential:\n{result.Stderr}");
    }
}
