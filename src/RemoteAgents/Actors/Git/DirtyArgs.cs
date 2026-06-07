using RemoteAgents.Engine.Operations;

namespace RemoteAgents.Actors.Git;

public sealed record DirtyArgs() : OperationArgs("git-dirty");
