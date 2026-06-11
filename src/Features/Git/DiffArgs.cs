using RemoteAgents.Domain.Flow.Operations;

namespace RemoteAgents.Features.Git;

public sealed record DiffArgs() : OperationArgs("git-diff");
