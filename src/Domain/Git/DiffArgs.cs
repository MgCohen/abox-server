using ABox.Infrastructure.Operations;

namespace ABox.Domain.Git;

public sealed record DiffArgs() : OperationArgs("git-diff");
