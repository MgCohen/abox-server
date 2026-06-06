using System.Text.Json;
using RemoteAgents.Actors.Agents.Claude;

namespace RemoteAgents.Tests;

public class ClaudeStopHookTests
{
    [Fact]
    public void Create_writes_a_settings_file_with_a_Stop_command_pointing_at_the_shim()
    {
        using var hook = ClaudeStopHook.Create();

        using var doc = JsonDocument.Parse(File.ReadAllText(hook.SettingsFile));
        var command = doc.RootElement
            .GetProperty("hooks").GetProperty("Stop")[0]
            .GetProperty("hooks")[0].GetProperty("command").GetString();

        Assert.NotNull(command);
        Assert.Contains("pwsh", command);
        Assert.Contains("stop-shim.ps1", command);
    }

    [Fact]
    public void The_shim_writes_the_payload_as_utf8_to_the_signal_env_var()
    {
        using var hook = ClaudeStopHook.Create();
        var shim = File.ReadAllText(Path.Combine(Path.GetDirectoryName(hook.SettingsFile)!, "stop-shim.ps1"));

        Assert.Contains(ClaudeStopHook.SignalEnvVar, shim);
        Assert.Contains("utf8", shim);
    }

    [Fact]
    public void HasFired_is_false_until_the_signal_file_has_content()
    {
        using var hook = ClaudeStopHook.Create();
        Assert.False(hook.HasFired);

        File.WriteAllText(hook.SignalFile, "{\"last_assistant_message\":\"hi\"}");
        Assert.True(hook.HasFired);
    }

    [Fact]
    public void ReadFinalMessage_extracts_last_assistant_message()
    {
        using var hook = ClaudeStopHook.Create();
        File.WriteAllText(hook.SignalFile,
            "{\"hook_event_name\":\"Stop\",\"last_assistant_message\":\"the final answer\"}");

        Assert.Equal("the final answer", hook.ReadFinalMessage());
    }

    [Fact]
    public void ReadFinalMessage_returns_null_when_absent_or_malformed()
    {
        using var hook = ClaudeStopHook.Create();
        Assert.Null(hook.ReadFinalMessage());

        File.WriteAllText(hook.SignalFile, "not json");
        Assert.Null(hook.ReadFinalMessage());
    }

    [Fact]
    public void Dispose_removes_the_temp_artifacts()
    {
        var hook = ClaudeStopHook.Create();
        var settings = hook.SettingsFile;
        Assert.True(File.Exists(settings));

        hook.Dispose();
        Assert.False(File.Exists(settings));
    }
}
