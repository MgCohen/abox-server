using RemoteAgents.Flows;

namespace RemoteAgents.Actors.Git;

public sealed record DirtyArgs() : OperationArgs("git-dirty");
