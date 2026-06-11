using RemoteAgents.Infrastructure.Operations;

namespace RemoteAgents.Domain.Git;

public sealed record CommitArgs(string Message, IReadOnlyList<string> Files, string? CoAuthor = null)
    : OperationArgs("git-commit");
