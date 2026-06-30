using System.Text.Json;

namespace ABox.Governance.Hooks.Tests.Unit;

public sealed class AgentActionTests
{
    private sealed class RecordingLauncher : IAgentLauncher
    {
        public string? Prompt { get; private set; }

        public Task<HookDispatchResult> LaunchAsync(HookManifest manifest, string prompt, HookEvent e, CancellationToken ct)
        {
            Prompt = prompt;
            return Task.FromResult(new HookDispatchResult(manifest.Path, manifest.Mode, 0, false, null, "fresh review: ok", ""));
        }
    }

    [Rule("HookDispatcher routes an agent: action to the agent launcher, not the shell, and relays its output")]
    [Fact]
    public async Task Agent_action_is_routed_to_the_launcher()
    {
        var launcher = new RecordingLauncher();
        var dispatcher = new HookDispatcher(new HookRunner(agentLauncher: launcher));

        var manifest = new HookManifest(
            "/feat/review.hook", [HookKind.TurnEnded], HookWhen.None, HookMode.Check,
            new HookAction.Agent("Review the changed doc."));
        var evt = new HookEvent(
            HookKind.TurnEnded, HookSource.Claude, "s1", "/repo",
            JsonDocument.Parse("{}").RootElement.Clone());

        var results = await dispatcher.DispatchAsync(evt, [manifest]);

        Assert.Equal("Review the changed doc.", launcher.Prompt);
        Assert.Single(results);
        Assert.Contains("fresh review: ok", results[0].Feedback);
    }
}
