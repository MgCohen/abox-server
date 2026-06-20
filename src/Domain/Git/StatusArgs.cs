using ABox.Infrastructure.Operations;

namespace ABox.Domain.Git;

public sealed record StatusArgs() : OperationArgs("git-status");
