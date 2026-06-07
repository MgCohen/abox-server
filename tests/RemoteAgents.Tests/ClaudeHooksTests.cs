using System.Text.Json;
using RemoteAgents.Actors.Agents.Claude;

namespace RemoteAgents.Tests;

public class ClaudeHooksTests
{
    [Fact]
    public void Create_writes_a_settings_file_with_a_Stop_command_pointing_at_the_shim()
    {
        using var hook = ClaudeHooks.Create();

        var command = Hooks(hook).GetProperty("Stop")[0]
            .GetProperty("hooks")[0].GetProperty("command").GetString();

        Assert.NotNull(command);
        Assert.Contains("pwsh", command);
        Assert.Contains("stop-shim.ps1", command);
    }

    [Fact]
    public void Create_without_gating_emits_no_PreToolUse_hook_and_no_permission_dir()
    {
        using var hook = ClaudeHooks.Create();

        Assert.False(Hooks(hook).TryGetProperty("PreToolUse", out _));
        Assert.Null(hook.PermissionDir);
    }

    [Fact]
    public void Create_with_gating_emits_a_PreToolUse_hook_matching_the_mutating_tools()
    {
        using var hook = ClaudeHooks.Create(gatePermissions: true);

        var group = Hooks(hook).GetProperty("PreToolUse")[0];
        var matcher = group.GetProperty("matcher").GetString()!;
        Assert.Contains("Bash", matcher);
        Assert.Contains("Write", matcher);
        Assert.Contains("Edit", matcher);

        var command = group.GetProperty("hooks")[0].GetProperty("command").GetString()!;
        Assert.Contains("perm-shim.ps1", command);
        Assert.NotNull(hook.PermissionDir);
    }

    [Fact]
    public void The_perm_shim_reads_payload_and_targets_the_permission_dir()
    {
        using var hook = ClaudeHooks.Create(gatePermissions: true);
        var shim = File.ReadAllText(Path.Combine(Path.GetDirectoryName(hook.SettingsFile)!, "perm-shim.ps1"));

        Assert.Contains(ClaudeHooks.PermissionEnvVar, shim);
        Assert.Contains("In.ReadToEnd", shim);
        Assert.Contains("permissionDecision", shim);
    }

    [Fact]
    public void DrainRequests_reads_each_request_file_exactly_once()
    {
        using var hook = ClaudeHooks.Create(gatePermissions: true);
        File.WriteAllText(Path.Combine(hook.PermissionDir!, "req-abc.json"), "{\"tool_name\":\"Bash\"}");

        var first = hook.DrainRequests();
        Assert.Equal("abc", Assert.Single(first).Id);
        Assert.Contains("Bash", first[0].Payload);

        Assert.Empty(hook.DrainRequests());
    }

    [Fact]
    public void DrainRequests_is_empty_when_gating_is_off()
    {
        using var hook = ClaudeHooks.Create();
        Assert.Empty(hook.DrainRequests());
    }

    [Fact]
    public void Respond_writes_the_decision_to_the_matching_response_file()
    {
        using var hook = ClaudeHooks.Create(gatePermissions: true);
        var request = new PermissionRequest("abc", "{}");

        hook.Respond(request, "{\"decision\":\"allow\"}");

        var resp = Path.Combine(hook.PermissionDir!, "resp-abc.json");
        Assert.True(File.Exists(resp));
        Assert.Contains("allow", File.ReadAllText(resp));
    }

    [Fact]
    public void Respond_throws_when_gating_is_off()
    {
        using var hook = ClaudeHooks.Create();
        Assert.Throws<InvalidOperationException>(() => hook.Respond(new PermissionRequest("x", "{}"), "{}"));
    }

    [Fact]
    public void HasFired_is_false_until_the_signal_file_has_content()
    {
        using var hook = ClaudeHooks.Create();
        Assert.False(hook.HasFired);

        File.WriteAllText(hook.SignalFile, "{\"last_assistant_message\":\"hi\"}");
        Assert.True(hook.HasFired);
    }

    [Fact]
    public void ReadFinalMessage_extracts_last_assistant_message()
    {
        using var hook = ClaudeHooks.Create();
        File.WriteAllText(hook.SignalFile,
            "{\"hook_event_name\":\"Stop\",\"last_assistant_message\":\"the final answer\"}");

        Assert.Equal("the final answer", hook.ReadFinalMessage());
    }

    [Fact]
    public void ReadFinalMessage_returns_null_when_absent_or_malformed()
    {
        using var hook = ClaudeHooks.Create();
        Assert.Null(hook.ReadFinalMessage());

        File.WriteAllText(hook.SignalFile, "not json");
        Assert.Null(hook.ReadFinalMessage());
    }

    [Fact]
    public void Dispose_removes_the_temp_artifacts()
    {
        var hook = ClaudeHooks.Create();
        var settings = hook.SettingsFile;
        Assert.True(File.Exists(settings));

        hook.Dispose();
        Assert.False(File.Exists(settings));
    }

    private static JsonElement Hooks(ClaudeHooks hook)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(hook.SettingsFile));
        return doc.RootElement.GetProperty("hooks").Clone();
    }
}
