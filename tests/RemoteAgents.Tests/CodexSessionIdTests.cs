using RemoteAgents.Actors.Agents.Codex;

namespace RemoteAgents.Tests;

public class CodexSessionIdTests
{
    [Theory]
    [InlineData("{\"thread_id\":\"abcd1234ef\"}")]
    [InlineData("{\"session_id\":\"abcd1234ef\"}")]
    [InlineData("{\"sessionId\":\"abcd1234ef\"}")]
    [InlineData("{\"thread\":{\"id\":\"abcd1234ef\"}}")]
    [InlineData("{\"session\":{\"id\":\"abcd1234ef\"}}")]
    [InlineData("{\"payload\":{\"thread_id\":\"abcd1234ef\"}}")]
    public void Finds_the_session_id_across_known_shapes(string line)
    {
        Assert.Equal("abcd1234ef", CodexSessionId.Scan(line));
    }

    [Theory]
    [InlineData("codex_core::exec ERROR not json")]
    [InlineData("{\"thread_id\":\"short\"}")]
    [InlineData("{\"other\":\"abcd1234ef\"}")]
    public void Returns_null_when_no_id_present(string line)
    {
        Assert.Null(CodexSessionId.Scan(line));
    }
}
