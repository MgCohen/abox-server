using System.Text.Json;
using ABox.Domain.Agents.Claude;

namespace ABox.Tests.Unit.Tests;

public class ClaudeHooksTests
{
    [Rule("ClaudeHooks.Create → settings file wires a Stop hook to the stop-shim")]
    [Fact]
    public void Create_writes_a_settings_file_with_a_Stop_command_pointing_at_the_shim()
    {
        using var hook = ClaudeHooks.Create();

        var command = Hooks(hook).GetProperty("Stop")[0]
            .GetProperty("hooks")[0].GetProperty("command").GetString();

        Assert.NotNull(command);
        Assert.StartsWith("sh ", command);
        Assert.Contains("stop-shim.sh", command);
    }

    [Rule("ClaudeHooks.Create without gating → no PreToolUse hook and no permission dir")]
    [Fact]
    public void Create_without_gating_emits_no_PreToolUse_hook_and_no_permission_dir()
    {
        using var hook = ClaudeHooks.Create();

        Assert.False(Hooks(hook).TryGetProperty("PreToolUse", out _));
        Assert.Null(hook.PermissionDir);
    }

    [Rule("ClaudeHooks.Create with gating → PreToolUse perm-shim hook matching the mutating tools and a permission dir")]
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
        Assert.Contains("perm-shim.sh", command);
        Assert.NotNull(hook.PermissionDir);
    }

    [Rule("perm-shim script → reads the stdin payload and emits a permissionDecision against the permission dir")]
    [Fact]
    public void The_perm_shim_reads_payload_and_targets_the_permission_dir()
    {
        using var hook = ClaudeHooks.Create(gatePermissions: true);
        var shim = File.ReadAllText(Path.Combine(Path.GetDirectoryName(hook.SettingsFile)!, "perm-shim.sh"));

        Assert.Contains(ClaudeHooks.PermissionEnvVar, shim);
        Assert.Contains("payload=$(cat)", shim);
        Assert.Contains("permissionDecision", shim);
    }

    [Rule("ClaudeHooks.DrainRequests → returns each pending request once with its id and payload, then nothing")]
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

    [Rule("ClaudeHooks.DrainRequests with gating off → returns empty")]
    [Fact]
    public void DrainRequests_is_empty_when_gating_is_off()
    {
        using var hook = ClaudeHooks.Create();
        Assert.Empty(hook.DrainRequests());
    }

    [Rule("ClaudeHooks.Respond → writes the decision to the response file matching the request id")]
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

    [Rule("ClaudeHooks.Respond with gating off → throws InvalidOperationException")]
    [Fact]
    public void Respond_throws_when_gating_is_off()
    {
        using var hook = ClaudeHooks.Create();
        Assert.Throws<InvalidOperationException>(() => hook.Respond(new PermissionRequest("x", "{}"), "{}"));
    }

    [Rule("ClaudeHooks.HasFired → reflects whether the signal file has content")]
    [Fact]
    public void HasFired_is_false_until_the_signal_file_has_content()
    {
        using var hook = ClaudeHooks.Create();
        Assert.False(hook.HasFired);

        File.WriteAllText(hook.SignalFile, "{\"last_assistant_message\":\"hi\"}");
        Assert.True(hook.HasFired);
    }

    [Rule("ClaudeHooks.ReadFinalMessage → returns the last_assistant_message from the signal file")]
    [Fact]
    public void ReadFinalMessage_extracts_last_assistant_message()
    {
        using var hook = ClaudeHooks.Create();
        File.WriteAllText(hook.SignalFile,
            "{\"hook_event_name\":\"Stop\",\"last_assistant_message\":\"the final answer\"}");

        Assert.Equal("the final answer", hook.ReadFinalMessage());
    }

    [Rule("ClaudeHooks.ReadFinalMessage with missing or malformed signal → returns null")]
    [Fact]
    public void ReadFinalMessage_returns_null_when_absent_or_malformed()
    {
        using var hook = ClaudeHooks.Create();
        Assert.Null(hook.ReadFinalMessage());

        File.WriteAllText(hook.SignalFile, "not json");
        Assert.Null(hook.ReadFinalMessage());
    }

    [Rule("ClaudeHooks.Dispose → removes the temp artifacts it created")]
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
