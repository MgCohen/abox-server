using ABox.Infrastructure.Operations;

namespace ABox.Domain.Git;

public sealed record DirtyArgs() : OperationArgs("git-dirty");
