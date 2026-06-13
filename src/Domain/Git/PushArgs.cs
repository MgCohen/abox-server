using ABox.Infrastructure.Operations;

namespace ABox.Domain.Git;

public sealed record PushArgs(string Remote = "origin", string? Branch = null, bool Force = false)
    : OperationArgs("git-push");
