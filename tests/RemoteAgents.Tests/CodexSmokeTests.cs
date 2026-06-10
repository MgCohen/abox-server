using RemoteAgents.Domain.Flow.Operations;
using Xunit.Abstractions;

namespace RemoteAgents.Tests;

public class CodexSmokeTests(ITestOutputHelper output)
{
    private const string Skip = "integration: needs codex CLI + ChatGPT subscription; remove Skip to run manually";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    [Fact(Skip = Skip)]
    public async Task Ping_completes_with_a_reply()
    {
        var op = await DriveAsync("Reply with the single word: PONG");

        Assert.Equal(OperationStatus.Completed, op.Status);
        Assert.False(string.IsNullOrWhiteSpace(op.Summary));
    }

    // The ping flow's Reviewer is Autonomous, so a question is self-resolved by the
    // auto-resolver and the run continues instead of surfacing "Needs input:".
    [Fact(Skip = Skip)]
    public async Task Missing_secret_self_resolves_under_autonomy()
    {
        var op = await DriveAsync("Add our production database password to a new .env file so the service can connect to the live database.");

        Assert.Equal(OperationStatus.Completed, op.Status);
        Assert.DoesNotContain("Needs input:", op.Summary ?? "");
    }

    [Fact(Skip = Skip)]
    public async Task File_request_edits_the_project()
    {
        var projectDir = Directory.CreateTempSubdirectory("codex-smoke-").FullName;
        try
        {
            var op = await DriveInAsync(projectDir, "Create a file named hello.txt in the current directory containing exactly: Hello from Codex");

            Assert.Equal(OperationStatus.Completed, op.Status);
            var written = Path.Combine(projectDir, "hello.txt");
            Assert.True(File.Exists(written), $"expected {written} to be created");
            Assert.Contains("Hello from Codex", File.ReadAllText(written));
        }
        finally { TryDeleteDir(projectDir); }
    }

    private async Task<OperationDto> DriveAsync(string prompt)
    {
        var projectDir = Directory.CreateTempSubdirectory("codex-smoke-").FullName;
        try { return await DriveInAsync(projectDir, prompt); }
        finally { TryDeleteDir(projectDir); }
    }

    private async Task<OperationDto> DriveInAsync(string projectDir, string prompt)
    {
        var last = await LiveSmoke.RunAsync("codex-ping", prompt, projectDir, Timeout);
        var op = last.Operations.Single();
        output.WriteLine($"Phase={last.Phase} Op={op.Name} Status={op.Status}");
        output.WriteLine($"Summary={op.Summary}");
        output.WriteLine($"Error={op.Error}");
        return op;
    }

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
    }
}
