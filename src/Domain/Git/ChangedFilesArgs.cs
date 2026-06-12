using ABox.Infrastructure.Operations;

namespace ABox.Domain.Git;

public sealed record ChangedFilesArgs() : OperationArgs("git-changed-files");
