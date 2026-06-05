using RemoteAgents.Flows;

namespace RemoteAgents.Actors.Git;

public sealed record CommitArgs(string Message, IReadOnlyList<string> Files, string? CoAuthor = null)
    : OperationArgs("git-commit");
