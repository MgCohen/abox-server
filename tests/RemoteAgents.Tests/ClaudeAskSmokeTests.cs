using RemoteAgents.Actors.Agents;
using RemoteAgents.Actors.Agents.Claude;
using Xunit.Abstractions;

namespace RemoteAgents.Tests;

// Live validation for PermissionPolicy.Ask: proves the PreToolUse hook fires over
// ConPTY, carries the tool payload to the resolver, and that the resolver's
// allow/deny decision is honored. These are the "can the input detect a
// permission request" cells (plan §7). Skip-gated like the rest of the matrix.
public class ClaudeAskSmokeTests(ITestOutputHelper output)
{
    private const string Skip = "integration: needs claude CLI + Max subscription; remove Skip to run manually";
    private const string CreateFilePrompt =
        "Create a file named hello.txt in the current directory containing exactly: Hello from Claude";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    [Fact(Skip = Skip)]
    public async Task Ask_detects_the_request_and_allow_lets_the_write_through()
    {
        var resolver = new RecordingResolver("Allow");
        var projectDir = Directory.CreateTempSubdirectory("claude-ask-").FullName;
        try
        {
            await DriveAsync(resolver, projectDir, CreateFilePrompt);

            AssertGatedToolWasAskedAbout(resolver);
            var written = Path.Combine(projectDir, "hello.txt");
            Assert.True(File.Exists(written), $"expected {written} to be created after Allow");
        }
        finally { TryDeleteDir(projectDir); }
    }

    // null is exactly what NonInteractiveResolver returns: the deny-on-null path
    // that replaces acceptEdits' silent mid-turn hang.
    [Fact(Skip = Skip)]
    public async Task Ask_deny_blocks_the_write_without_hanging()
    {
        var resolver = new RecordingResolver(null);
        var projectDir = Directory.CreateTempSubdirectory("claude-ask-").FullName;
        try
        {
            await DriveAsync(resolver, projectDir, CreateFilePrompt);

            AssertGatedToolWasAskedAbout(resolver);
            var written = Path.Combine(projectDir, "hello.txt");
            Assert.False(File.Exists(written), "file must not be created when the gated tool is denied");
        }
        finally { TryDeleteDir(projectDir); }
    }

    private void AssertGatedToolWasAskedAbout(RecordingResolver resolver)
    {
        Assert.NotEmpty(resolver.Questions);
        Assert.All(resolver.Questions, q => Assert.IsType<AgentQuestion.Choice>(q));
        foreach (var q in resolver.Questions) output.WriteLine($"gated: {q.Prompt}");
    }

    private async Task DriveAsync(IQuestionResolver resolver, string projectDir, string prompt)
    {
        var config = new ClaudeConfig("asker", "Asks before acting.", "", "You implement.", PermissionPolicy.Ask);
        var provider = new ClaudeProvider(config, resolver);

        using var cts = new CancellationTokenSource(Timeout);
        var drive = await provider.DriveAsync(new AgentRunRequest(prompt, projectDir), cts.Token);

        output.WriteLine($"exit={drive.ExitCode}");
        output.WriteLine($"text={drive.Text}");
    }

    private sealed class RecordingResolver(string? answer) : IQuestionResolver
    {
        public List<AgentQuestion> Questions { get; } = [];

        public Task<string?> ResolveAsync(AgentQuestion question, CancellationToken ct)
        {
            Questions.Add(question);
            return Task.FromResult(answer);
        }
    }

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
    }
}
