using ABox.Infrastructure.Operations;

namespace ABox.Domain.Git;

public sealed record RebaseOntoArgs(string NewBase, string OldBase, string Branch)
    : OperationArgs("git-rebase-onto");
