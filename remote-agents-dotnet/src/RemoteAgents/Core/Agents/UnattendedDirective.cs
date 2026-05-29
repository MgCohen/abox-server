namespace RemoteAgents.Agents;

// Text appended to the agent's system prompt when InteractionMode is
// NonInteractive (the default). Tells the model that there is no user
// available to answer questions this turn — either make an assumption and
// continue, or emit the Sentinel and stop.
//
// The sentinel doubles as the primary OpenQuestion signal for Codex
// (which has no dedicated "idle waiting on text" hook event) — see
// CodexHookParser. Keeping both readers pointed at this single constant
// guarantees the model's escape hatch matches what the parser scans for.
public static class UnattendedDirective
{
    public const string Sentinel = "<<NEEDS_INPUT>>";

    public static readonly string SystemPromptAddendum =
$"""
You are running in UNATTENDED mode. There is no user available to answer
clarifying questions during this turn.

If information is missing or ambiguous, make a reasonable assumption,
state the assumption explicitly in your response, and continue with the
work.

If you absolutely cannot proceed without user input — e.g. you need a
secret, a destination path you can't infer, or a binary yes/no the
project documents nowhere — emit the literal token {Sentinel} on its
own line, followed by exactly the questions you would ask, then stop.

Do not ask questions in any other form. Do not end your turn with "?".
Do not say "let me know" or "please confirm." Either decide and continue,
or emit {Sentinel} and stop.
""";

    // Return the system prompt the agent should actually send. In
    // Interactive mode the user's system prompt is returned unchanged
    // (or null). In NonInteractive mode the directive is appended (or
    // returned standalone if the user had none).
    public static string? Compose(string? userSystemPrompt, InteractionMode mode)
    {
        if (mode == InteractionMode.Interactive) return userSystemPrompt;
        return string.IsNullOrEmpty(userSystemPrompt)
            ? SystemPromptAddendum
            : userSystemPrompt + "\n\n" + SystemPromptAddendum;
    }
}
