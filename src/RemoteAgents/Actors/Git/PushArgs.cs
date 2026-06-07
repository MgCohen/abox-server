using RemoteAgents.Engine.Operations;

namespace RemoteAgents.Actors.Git;

public sealed record PushArgs(string Remote = "origin", string? Branch = null, bool Force = false)
    : OperationArgs("git-push");
