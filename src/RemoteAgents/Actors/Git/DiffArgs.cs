using RemoteAgents.Engine.Operations;

namespace RemoteAgents.Actors.Git;

public sealed record DiffArgs() : OperationArgs("git-diff");
