namespace Infra.AgentRuntime;

public interface IApiHandler<in TRequest, TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken ct);
}
