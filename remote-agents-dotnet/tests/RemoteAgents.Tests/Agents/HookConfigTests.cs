using System.Text.Json;
using RemoteAgents.Agents;

namespace RemoteAgents.Tests.Agents;

public class HookConfigTests : IDisposable
{
    private readonly string _root;
    private readonly string _shimPath;

    public HookConfigTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ra-hookcfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _shimPath = Path.Combine(_root, "hookshim.ps1");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    // ---- Claude ----

    [Fact]
    public void Claude_install_writes_settings_with_all_matchers_and_shim_command()
    {
        var projectDir = Path.Combine(_root, "proj");
        Directory.CreateDirectory(projectDir);

        ClaudeHookConfig.Install(projectDir, _shimPath);

        var settingsPath = Path.Combine(projectDir, ClaudeHookConfig.SettingsRelative);
        Assert.True(File.Exists(settingsPath));

        using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
        var hooks = doc.RootElement.GetProperty("hooks");

        var notif = hooks.GetProperty("Notification").EnumerateArray().ToList();
        Assert.Equal(3, notif.Count);
        var matchers = notif.Select(e => e.GetProperty("matcher").GetString()).ToArray();
        Assert.Contains("idle_prompt",        matchers);
        Assert.Contains("permission_prompt",  matchers);
        Assert.Contains("elicitation_dialog", matchers);

        Assert.Equal(1, hooks.GetProperty("Stop").GetArrayLength());
        Assert.Equal(1, hooks.GetProperty("StopFailure").GetArrayLength());

        var cmd = notif[0].GetProperty("hooks")[0].GetProperty("command").GetString()!;
        Assert.Contains("pwsh", cmd);
        Assert.Contains("hookshim.ps1", cmd);
        Assert.Contains("claude.", cmd);
    }

    [Fact]
    public void Claude_install_preserves_existing_settings_as_backup()
    {
        var projectDir = Path.Combine(_root, "proj");
        Directory.CreateDirectory(Path.Combine(projectDir, ".claude"));
        var settingsPath = Path.Combine(projectDir, ClaudeHookConfig.SettingsRelative);
        File.WriteAllText(settingsPath, """{"user":"settings"}""");

        ClaudeHookConfig.Install(projectDir, _shimPath);

        Assert.True(File.Exists(settingsPath + ".ra-bak"));
        Assert.Equal("""{"user":"settings"}""", File.ReadAllText(settingsPath + ".ra-bak"));
        Assert.Contains("Notification", File.ReadAllText(settingsPath));
    }

    [Fact]
    public void Claude_install_twice_does_not_clobber_original_backup()
    {
        var projectDir = Path.Combine(_root, "proj");
        Directory.CreateDirectory(Path.Combine(projectDir, ".claude"));
        var settingsPath = Path.Combine(projectDir, ClaudeHookConfig.SettingsRelative);
        File.WriteAllText(settingsPath, """{"original":true}""");

        ClaudeHookConfig.Install(projectDir, _shimPath);
        ClaudeHookConfig.Install(projectDir, _shimPath);

        Assert.Equal("""{"original":true}""", File.ReadAllText(settingsPath + ".ra-bak"));
    }

    [Fact]
    public void Claude_uninstall_restores_original_settings()
    {
        var projectDir = Path.Combine(_root, "proj");
        Directory.CreateDirectory(Path.Combine(projectDir, ".claude"));
        var settingsPath = Path.Combine(projectDir, ClaudeHookConfig.SettingsRelative);
        File.WriteAllText(settingsPath, """{"original":true}""");

        ClaudeHookConfig.Install(projectDir, _shimPath);
        ClaudeHookConfig.Uninstall(projectDir);

        Assert.True(File.Exists(settingsPath));
        Assert.False(File.Exists(settingsPath + ".ra-bak"));
        Assert.Equal("""{"original":true}""", File.ReadAllText(settingsPath));
    }

    [Fact]
    public void Claude_uninstall_with_no_prior_settings_removes_file()
    {
        var projectDir = Path.Combine(_root, "proj");
        Directory.CreateDirectory(projectDir);

        ClaudeHookConfig.Install(projectDir, _shimPath);
        ClaudeHookConfig.Uninstall(projectDir);

        Assert.False(File.Exists(Path.Combine(projectDir, ClaudeHookConfig.SettingsRelative)));
    }

    // ---- Codex ----

    [Fact]
    public void Codex_install_writes_hooks_with_nested_matcher_and_shim_command()
    {
        var codexDir = Path.Combine(_root, ".codex");

        CodexHookConfig.Install(codexDir, _shimPath);

        var hooksPath = Path.Combine(codexDir, CodexHookConfig.HooksRelative);
        Assert.True(File.Exists(hooksPath));

        using var doc = JsonDocument.Parse(File.ReadAllText(hooksPath));
        var hooks = doc.RootElement.GetProperty("hooks");

        Assert.Equal(1, hooks.GetProperty("PermissionRequest").GetArrayLength());
        Assert.Equal(1, hooks.GetProperty("Stop").GetArrayLength());
        Assert.Equal(1, hooks.GetProperty("StopFailure").GetArrayLength());

        var permReq = hooks.GetProperty("PermissionRequest")[0];
        Assert.Equal("*", permReq.GetProperty("matcher").GetString());
        var inner = permReq.GetProperty("hooks")[0];
        Assert.Equal("command", inner.GetProperty("type").GetString());
        var cmd = inner.GetProperty("command").GetString()!;
        Assert.Contains("pwsh", cmd);
        Assert.Contains("codex.permission_request", cmd);
    }

    [Fact]
    public void Codex_install_preserves_existing_hooks_as_backup_and_uninstall_restores()
    {
        var codexDir = Path.Combine(_root, ".codex");
        Directory.CreateDirectory(codexDir);
        var hooksPath = Path.Combine(codexDir, CodexHookConfig.HooksRelative);
        File.WriteAllText(hooksPath, """{"user":"hooks"}""");

        CodexHookConfig.Install(codexDir, _shimPath);
        Assert.Equal("""{"user":"hooks"}""", File.ReadAllText(hooksPath + ".ra-bak"));

        CodexHookConfig.Uninstall(codexDir);
        Assert.True(File.Exists(hooksPath));
        Assert.False(File.Exists(hooksPath + ".ra-bak"));
        Assert.Equal("""{"user":"hooks"}""", File.ReadAllText(hooksPath));
    }

    [Fact]
    public void Codex_default_config_dir_is_under_user_profile()
    {
        var dir = CodexHookConfig.DefaultConfigDir();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, ".codex"), dir);
    }
}
