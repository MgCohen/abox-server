using RemoteAgents.Infrastructure.Operations;

namespace RemoteAgents.Domain.Git;

public sealed record PullArgs(string Remote = "origin", string? Branch = null, bool Rebase = false)
    : OperationArgs("git-pull");
