using RemoteAgents.Infrastructure.Operations;

namespace RemoteAgents.Domain.Git;

public sealed record DirtyArgs() : OperationArgs("git-dirty");
