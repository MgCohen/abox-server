using ABox.Domain.Agents;

namespace ABox.Tests.Support;

// Returns each scripted reply in turn, so a test can drive an agent through an
// ask-then-answer sequence without a live CLI.
internal sealed class ScriptedProvider(Queue<string> replies) : IProvider
{
    public ScriptedProvider(params string[] replies) : this(new Queue<string>(replies)) { }

    public Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
    {
        var text = replies.Dequeue();
        return Task.FromResult(new DriveResult(text, request.SessionId ?? "s1", 0, text, []));
    }
}
