using RemoteAgents.Engine.Operations;

namespace RemoteAgents.Actors.Git;

public sealed record ChangedFilesArgs() : OperationArgs("git-changed-files");
