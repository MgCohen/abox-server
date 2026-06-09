namespace Infra.AgentRuntime;

public interface IPipelineBehavior
{
    Task<TResponse> Run<TRequest, TResponse>(TRequest request, Func<Task<TResponse>> next, CancellationToken ct);
}
