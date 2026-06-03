using RemoteAgents.Actors.Agents;
using RemoteAgents.Actors.Agents.Codex;
using Xunit.Abstractions;

namespace RemoteAgents.Tests;

public class CodexSmokeTests(ITestOutputHelper output)
{
    [Fact(Skip = "integration: needs codex CLI + ChatGPT subscription; remove Skip to run manually")]
    public async Task Drives_a_real_codex_exec_and_returns_a_well_formed_result()
    {
        var tmp = Directory.CreateTempSubdirectory("codex-smoke-").FullName;
        try
        {
            var config = new CodexConfig("smoke", "smoke", "gpt-5.5", "", Sandbox: "read-only");
            var request = new AgentRunRequest("Reply with exactly: PONG", tmp);

            var result = await new CodexProvider(config).DriveAsync(request, CancellationToken.None);

            output.WriteLine($"ExitCode={result.ExitCode}");
            output.WriteLine($"SessionId={result.SessionId}");
            output.WriteLine($"Text={result.Text}");
            output.WriteLine($"Turns={result.Transcript.Count}");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("PONG", result.Text);
            Assert.True(result.SessionId.Length >= 8, "expected a sniffed session id");
            Assert.Contains(result.Transcript, t => t.Kind == AgentTurnKind.Text && t.Body.Contains("PONG"));
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { /* best-effort */ } }
    }
}
