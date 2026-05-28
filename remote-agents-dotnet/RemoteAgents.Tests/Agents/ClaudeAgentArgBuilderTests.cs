using RemoteAgents.Agents;

namespace RemoteAgents.Tests.Agents;

public class ClaudeAgentArgBuilderTests
{
    [Fact]
    public void Fresh_session_uses_session_id_flag()
    {
        var args = ClaudeAgent.BuildClaudeArgs("abc-123", isResume: false, new ClaudeAgentOptions());
        Assert.Equal("--session-id", args[0]);
        Assert.Equal("abc-123", args[1]);
        Assert.DoesNotContain("--resume", args);
    }

    [Fact]
    public void Resume_uses_resume_flag()
    {
        var args = ClaudeAgent.BuildClaudeArgs("abc-123", isResume: true, new ClaudeAgentOptions());
        Assert.Equal("--resume", args[0]);
        Assert.Equal("abc-123", args[1]);
        Assert.DoesNotContain("--session-id", args);
    }

    [Fact]
    public void Permission_mode_emitted_by_default()
    {
        var args = ClaudeAgent.BuildClaudeArgs("abc-123", false, new ClaudeAgentOptions());
        var i = args.IndexOf("--permission-mode");
        Assert.True(i >= 0);
        Assert.Equal("acceptEdits", args[i + 1]);
    }

    [Fact]
    public void Model_omitted_when_null()
    {
        var args = ClaudeAgent.BuildClaudeArgs("abc-123", false, new ClaudeAgentOptions(Model: null));
        Assert.DoesNotContain("--model", args);
    }

    [Fact]
    public void Model_emitted_when_set()
    {
        var args = ClaudeAgent.BuildClaudeArgs("abc-123", false, new ClaudeAgentOptions(Model: "opus"));
        var i = args.IndexOf("--model");
        Assert.True(i >= 0);
        Assert.Equal("opus", args[i + 1]);
    }

    // Step-5 acceptance: --append-system-prompt set iff SystemPrompt != null.
    [Fact]
    public void Append_system_prompt_omitted_when_null()
    {
        var args = ClaudeAgent.BuildClaudeArgs("abc-123", false, new ClaudeAgentOptions(SystemPrompt: null));
        Assert.DoesNotContain("--append-system-prompt", args);
    }

    [Fact]
    public void Append_system_prompt_emitted_when_set()
    {
        var args = ClaudeAgent.BuildClaudeArgs("abc-123", false, new ClaudeAgentOptions(SystemPrompt: "You are terse."));
        var i = args.IndexOf("--append-system-prompt");
        Assert.True(i >= 0);
        Assert.Equal("You are terse.", args[i + 1]);
    }
}
