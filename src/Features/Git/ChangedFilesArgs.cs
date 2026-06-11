using RemoteAgents.Domain.Flow.Operations;

namespace RemoteAgents.Features.Git;

public sealed record ChangedFilesArgs() : OperationArgs("git-changed-files");
