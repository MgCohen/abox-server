using ABox.Domain.Agents;

namespace ABox.Agents.Tests.Unit;

public class EnvScrubTests
{
    [Rule("EnvScrub maps each agent to its own billing keys → claude scrubs the Anthropic keys, codex the OpenAI key")]
    [Fact]
    public void EnvScrub_lists_each_agents_billing_keys()
    {
        Assert.Contains("ANTHROPIC_API_KEY", EnvScrub.ClaudeKeys);
        Assert.Contains("CLAUDE_API_KEY", EnvScrub.ClaudeKeys);
        Assert.DoesNotContain("OPENAI_API_KEY", EnvScrub.ClaudeKeys);

        Assert.Contains("OPENAI_API_KEY", EnvScrub.CodexKeys);
        Assert.DoesNotContain("ANTHROPIC_API_KEY", EnvScrub.CodexKeys);
    }
}
