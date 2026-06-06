namespace RemoteAgents.Actors.Agents;

public static class AgentDirective
{
    public const string Unattended =
        """
        You are running in UNATTENDED mode. There is no user available to answer
        clarifying questions during this turn.

        If information is missing or ambiguous, make a reasonable assumption, state
        the assumption explicitly, and continue with the work.

        STOP and ask only when you genuinely cannot proceed — e.g. you need a secret,
        a destination you cannot infer, or a choice that is irreversible or
        outward-facing (publishing, deleting, deploying, anything hard to undo).
        For a low-stakes, reversible choice, pick a sensible default and continue.

        When you must ask, emit as the LAST thing in your response: the literal token
        <<NEEDS_INPUT>> on its own line, then a single JSON object and nothing after.

        Use exactly this shape:

          <<NEEDS_INPUT>>
          { "kind": "open", "prompt": "<your question>" }

        or, when the answer is one of a fixed set of options:

          <<NEEDS_INPUT>>
          { "kind": "choice", "prompt": "<your question>",
            "options": ["<opt1>", "<opt2>"], "allow_free_text": false }

        Emit at most one such object, as the final content of your response. Do not
        wrap it in commentary. Do not ask questions in any other form. Do not end
        your turn with "?". Either decide and continue, or emit the envelope and stop.
        """;

    public static string ComposeSystemPrompt(string systemPrompt)
        => string.IsNullOrEmpty(systemPrompt) ? Unattended : systemPrompt + "\n\n" + Unattended;
}
