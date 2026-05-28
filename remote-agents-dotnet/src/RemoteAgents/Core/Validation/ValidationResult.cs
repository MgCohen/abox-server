namespace RemoteAgents.Validation;

public sealed record ValidationResult(
    bool Ok,
    string Summary,
    string Errors);
