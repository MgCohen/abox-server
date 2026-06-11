using RemoteAgents.Infrastructure.Operations;

namespace RemoteAgents.Domain.Git;

public sealed record ChangedFilesArgs() : OperationArgs("git-changed-files");
