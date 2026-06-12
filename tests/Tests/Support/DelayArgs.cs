using ABox.Infrastructure.Operations;

namespace ABox.Tests.Support;

// PROVISIONAL stub args — a placeholder step for the walking-skeleton flows, kept as a test fixture.
public sealed record DelayArgs(string Name, int DelayMs, string Result) : OperationArgs(Name);
