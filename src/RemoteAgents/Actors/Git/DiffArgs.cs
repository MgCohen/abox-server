using RemoteAgents.Flows;

namespace RemoteAgents.Actors.Git;

public sealed record DiffArgs() : OperationArgs("git-diff");
