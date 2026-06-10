using RemoteAgents.Domain.Flow.Operations;

namespace RemoteAgents.Features.Git;

public sealed record PushArgs(string Remote = "origin", string? Branch = null, bool Force = false)
    : OperationArgs("git-push");
