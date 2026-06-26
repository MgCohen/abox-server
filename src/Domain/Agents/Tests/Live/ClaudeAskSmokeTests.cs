using ABox.Domain.Agents;
using ABox.Domain.Agents.Claude;
using ABox.Host;
using Xunit.Abstractions;

namespace ABox.Agents.Tests.Live;

// Live validation for PermissionPolicy.Ask: proves the PreToolUse hook fires over
// ConPTY, carries the tool payload to the resolver, and that the resolver's
// allow/deny decision is honored. These are the "can the input detect a
// permission request" cells (plan §7). Gated by [LiveFact] — runs only under RUN_LIVE=1.
public class ClaudeAskSmokeTests(ITestOutputHelper output)
{
    private const string CreateFilePrompt =
        "Create a file named hello.txt in the current directory containing exactly: Hello from Claude";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    [Rule("Ask policy with a gated tool and Allow → the hook fires and the write goes through")]
    [LiveFact]
    public async Task Ask_detects_the_request_and_allow_lets_the_write_through()
    {
        var resolver = new RecordingResolver("Allow");
        var projectDir = Directory.CreateTempSubdirectory("claude-ask-").FullName;
        try
        {
            await DriveAsync(PermissionPolicy.Ask, resolver, projectDir, CreateFilePrompt);

            AssertGatedToolWasAskedAbout(resolver);
            var written = Path.Combine(projectDir, "hello.txt");
            Assert.True(File.Exists(written), $"expected {written} to be created after Allow");
        }
        finally { TryDeleteDir(projectDir); }
    }

    // Auto auto-approves through the same gate without a human: the write runs even
    // though the resolver would have denied, and the resolver is never consulted.
    [Rule("Auto policy with a gated tool → the tool runs without consulting the resolver")]
    [LiveFact]
    public async Task Auto_runs_the_gated_tool_without_consulting_the_resolver()
    {
        var resolver = new RecordingResolver("Deny");
        var projectDir = Directory.CreateTempSubdirectory("claude-auto-").FullName;
        try
        {
            await DriveAsync(PermissionPolicy.Auto, resolver, projectDir, CreateFilePrompt);

            Assert.Empty(resolver.Questions);
            var written = Path.Combine(projectDir, "hello.txt");
            Assert.True(File.Exists(written), $"expected {written} to be created under Auto");
        }
        finally { TryDeleteDir(projectDir); }
    }

    // null is exactly what NonInteractiveResolver returns: the deny-on-null path
    // that replaces acceptEdits' silent mid-turn hang.
    [Rule("Ask policy with a gated tool and a denial → the write is blocked and the run does not hang")]
    [LiveFact]
    public async Task Ask_deny_blocks_the_write_without_hanging()
    {
        var resolver = new RecordingResolver(null);
        var projectDir = Directory.CreateTempSubdirectory("claude-ask-").FullName;
        try
        {
            await DriveAsync(PermissionPolicy.Ask, resolver, projectDir, CreateFilePrompt);

            AssertGatedToolWasAskedAbout(resolver);
            var written = Path.Combine(projectDir, "hello.txt");
            Assert.False(File.Exists(written), "file must not be created when the gated tool is denied");
        }
        finally { TryDeleteDir(projectDir); }
    }

    // The credential rides `docker run`, never the PTY-echoed `docker exec` line, so a real
    // billed turn's drive buffer must carry no token. Asserts the hardening end to end.
    [Rule("a credentialed live turn → the subscription token never appears in the drive buffer")]
    [LiveFact]
    public async Task Credential_never_appears_in_the_drive_buffer()
    {
        var projectDir = Directory.CreateTempSubdirectory("claude-leak-").FullName;
        try
        {
            var drive = await DriveAsync(PermissionPolicy.Auto, new RecordingResolver("Allow"), projectDir, CreateFilePrompt);

            Assert.DoesNotContain("sk-ant-", drive.RawOutput);
        }
        finally { TryDeleteDir(projectDir); }
    }

    private void AssertGatedToolWasAskedAbout(RecordingResolver resolver)
    {
        Assert.NotEmpty(resolver.Questions);
        Assert.All(resolver.Questions, q => Assert.IsType<AgentQuestion.Choice>(q));
        foreach (var q in resolver.Questions) output.WriteLine($"gated: {q.Prompt}");
    }

    private async Task<DriveResult> DriveAsync(PermissionPolicy policy, IDecisionResolver resolver, string projectDir, string prompt)
    {
        var config = new ClaudeConfig("asker", "Asks before acting.", "", "You implement.", policy);
        await using var provider = new ClaudeProvider(config, resolver, new AutoPolicy(), ClaudeBox.Confined());

        using var cts = new CancellationTokenSource(Timeout);
        var drive = await provider.DriveAsync(new AgentRunRequest(prompt, projectDir), cts.Token);

        output.WriteLine($"exit={drive.ExitCode}");
        output.WriteLine($"text={drive.Text}");
        return drive;
    }

    private sealed class RecordingResolver(string? answer) : IDecisionResolver
    {
        public List<AgentQuestion> Questions { get; } = [];

        public Resolution Source => Resolution.Human;

        public Task<string?> ResolveAsync(AgentQuestion question, DecisionKind kind, CancellationToken ct)
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
