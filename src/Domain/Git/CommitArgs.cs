using ABox.Infrastructure.Operations;

namespace ABox.Domain.Git;

public sealed record CommitArgs(string Message, IReadOnlyList<string> Files, string? CoAuthor = null)
    : OperationArgs("git-commit");
