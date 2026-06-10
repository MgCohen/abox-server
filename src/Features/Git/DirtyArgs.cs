using RemoteAgents.Domain.Flow.Operations;

namespace RemoteAgents.Features.Git;

public sealed record DirtyArgs() : OperationArgs("git-dirty");
