using ABox.Infrastructure.Operations;

namespace ABox.Domain.Git;

public sealed record PullArgs(string Remote = "origin", string? Branch = null, bool Rebase = false)
    : OperationArgs("git-pull");
