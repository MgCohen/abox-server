using RemoteAgents.Infrastructure.Operations;

namespace RemoteAgents.Domain.Git;

public sealed record PushArgs(string Remote = "origin", string? Branch = null, bool Force = false)
    : OperationArgs("git-push");
