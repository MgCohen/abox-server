namespace RemoteAgents.Flows;

// Parsed positional args for a flow invocation. Replaces the per-flow
// argv parsing in FlowBootstrap.StartAsync. The CLI dispatcher and the
// Host's in-process executor both produce this shape from their own
// argument source (command-line vs REST POST body).
public sealed record FlowArgs(
    string Project,
    string Prompt,
    string[] Extra,
    bool   Push = false);
