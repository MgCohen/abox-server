using RemoteAgents.Domain.Flow.Operations;

namespace RemoteAgents.Features.Git;

public sealed record CommitArgs(string Message, IReadOnlyList<string> Files, string? CoAuthor = null)
    : OperationArgs("git-commit");
