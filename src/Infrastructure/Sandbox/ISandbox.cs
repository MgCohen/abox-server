namespace ABox.Infrastructure.Sandbox;

public interface ISandbox : IAsyncDisposable
{
    Task<ExecResult> ExecAsync(string command, CancellationToken ct);
}
