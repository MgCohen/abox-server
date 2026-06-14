using ABox.Infrastructure.Operations;

namespace ABox.Domain.Agents.Judging;

public sealed record JudgeArgs(JudgeRequest Request) : OperationArgs(Request.Subject);
