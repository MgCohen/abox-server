namespace RemoteAgents.Validation;

public interface IValidator
{
    Task<ValidationResult> ValidateAsync(string projectDir, CancellationToken ct = default);
}
