using RemoteAgents.Infrastructure.Operations;

namespace RemoteAgents.Domain.Git;

public sealed record DiffArgs() : OperationArgs("git-diff");
