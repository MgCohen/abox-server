namespace ABox.Domain.Agents.Judging;

public sealed record Criterion(string Id, string Description, string? HowToCheck = null);
