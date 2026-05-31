using RemoteAgents.Providers.Codex;
using RemoteAgents.Agents;

namespace RemoteAgents.Tests.Agents;

public class CodexAgentArgBuilderTests
{
    [Fact]
    public void Fresh_session_has_exec_no_resume()
    {
        var args = CodexAgent.BuildCodexArgs(null, "C:/proj", "C:/tmp/last.txt", new CodexAgentOptions());
        Assert.Equal("exec", args[0]);
        Assert.DoesNotContain("resume", args);
    }

    [Fact]
    public void Resume_session_uses_exec_resume_id()
    {
        var args = CodexAgent.BuildCodexArgs("sess-abc", "C:/proj", "C:/tmp/last.txt", new CodexAgentOptions());
        Assert.Equal("exec", args[0]);
        Assert.Equal("resume", args[1]);
        Assert.Equal("sess-abc", args[2]);
    }

    [Fact]
    public void Includes_hook_trust_bypass_and_json_and_sandbox()
    {
        var args = CodexAgent.BuildCodexArgs(null, "C:/proj", "C:/tmp/last.txt", new CodexAgentOptions());
        // The old --dangerously-bypass-approvals-and-sandbox is gone — it
        // disabled hook invocation. exec is autonomous by default; we
        // only need to bypass the per-hook trust gate.
        Assert.DoesNotContain("--dangerously-bypass-approvals-and-sandbox", args);
        Assert.Contains("--dangerously-bypass-hook-trust", args);
        Assert.Contains("--skip-git-repo-check",          args);
        Assert.Contains("--json",                          args);
        var si = args.IndexOf("--sandbox");
        Assert.True(si >= 0);
        Assert.Equal("workspace-write", args[si + 1]);
    }

    [Fact]
    public void Stdin_dash_is_last()
    {
        var args = CodexAgent.BuildCodexArgs(null, "C:/proj", "C:/tmp/last.txt", new CodexAgentOptions());
        Assert.Equal("-", args.Last());
    }

    [Fact]
    public void Model_omitted_when_null()
    {
        var args = CodexAgent.BuildCodexArgs(null, "C:/proj", "C:/tmp/last.txt", new CodexAgentOptions(Model: null));
        Assert.DoesNotContain("--model", args);
    }

    [Fact]
    public void Model_emitted_when_set()
    {
        var args = CodexAgent.BuildCodexArgs(null, "C:/proj", "C:/tmp/last.txt", new CodexAgentOptions(Model: "gpt-5.5"));
        var i = args.IndexOf("--model");
        Assert.True(i >= 0);
        Assert.Equal("gpt-5.5", args[i + 1]);
    }
}

public class CodexAgentSessionIdScannerTests
{
    [Fact]
    public void Finds_thread_id_at_root() =>
        Assert.Equal("abc12345-thread", CodexSessionId.Scan("{\"thread_id\":\"abc12345-thread\"}"));

    [Fact]
    public void Finds_session_id_at_root() =>
        Assert.Equal("abc12345-sess", CodexSessionId.Scan("{\"session_id\":\"abc12345-sess\"}"));

    [Fact]
    public void Finds_camel_case_sessionId() =>
        Assert.Equal("abc12345-camel", CodexSessionId.Scan("{\"sessionId\":\"abc12345-camel\"}"));

    [Fact]
    public void Finds_nested_thread_id_in_thread_object() =>
        Assert.Equal("abc12345-nested", CodexSessionId.Scan("{\"thread\":{\"id\":\"abc12345-nested\"}}"));

    [Fact]
    public void Finds_payload_session_id() =>
        Assert.Equal("abc12345-payload", CodexSessionId.Scan("{\"payload\":{\"session_id\":\"abc12345-payload\"}}"));

    [Fact]
    public void Returns_null_for_non_json_lines()
    {
        Assert.Null(CodexSessionId.Scan(""));
        Assert.Null(CodexSessionId.Scan("not json"));
        Assert.Null(CodexSessionId.Scan("{\"foo\":\"bar\"}"));
    }

    [Fact]
    public void Rejects_too_short_ids()
    {
        // 7 chars — below the 8-char floor
        Assert.Null(CodexSessionId.Scan("{\"thread_id\":\"abc1234\"}"));
    }
}
