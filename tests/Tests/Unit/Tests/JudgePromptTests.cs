using ABox.Domain.Agents.Judging;

namespace ABox.Tests.Unit.Tests;

public class JudgePromptTests
{
    [Rule("JudgePrompt → lists every criterion id and instructs context-first")]
    [Fact]
    public void Prompt_lists_criteria_and_puts_context_first()
    {
        var request = new JudgeRequest(
            "a thing",
            [new("alpha", "first"), new("beta", "second")],
            "the blob",
            ["src/x.cs"]);

        var prompt = JudgePrompt.Compose(request);

        Assert.Contains("[alpha]", prompt);
        Assert.Contains("[beta]", prompt);
        Assert.Contains("Context (use this first)", prompt);
        Assert.Contains(JudgeParser.Sentinel, prompt);
    }
}
