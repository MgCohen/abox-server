using RemoteAgents.Agents;

namespace RemoteAgents.Tests.Agents;

public class UnattendedDirectiveTests
{
    [Fact]
    public void Compose_interactive_returns_user_prompt_unchanged_when_set()
    {
        var composed = UnattendedDirective.Compose("be terse", InteractionMode.Interactive);
        Assert.Equal("be terse", composed);
    }

    [Fact]
    public void Compose_interactive_passes_through_whatever_user_supplied()
    {
        Assert.Null(UnattendedDirective.Compose(null, InteractionMode.Interactive));
        Assert.Equal("", UnattendedDirective.Compose("", InteractionMode.Interactive));
    }

    [Fact]
    public void Compose_non_interactive_returns_directive_when_user_has_none()
    {
        var composed = UnattendedDirective.Compose(null, InteractionMode.NonInteractive);
        Assert.Equal(UnattendedDirective.SystemPromptAddendum, composed);
    }

    [Fact]
    public void Compose_non_interactive_appends_directive_to_existing_user_prompt()
    {
        var composed = UnattendedDirective.Compose("be terse", InteractionMode.NonInteractive);
        Assert.StartsWith("be terse",                                composed);
        Assert.EndsWith(UnattendedDirective.SystemPromptAddendum,    composed);
        Assert.Contains("\n\n",                                      composed);
    }

    [Fact]
    public void Directive_text_contains_the_sentinel()
    {
        Assert.Contains(UnattendedDirective.Sentinel, UnattendedDirective.SystemPromptAddendum);
    }

    [Fact]
    public void Codex_parser_sentinel_matches_directive_sentinel()
    {
        Assert.Equal(UnattendedDirective.Sentinel, CodexHookParser.Sentinel);
    }
}
