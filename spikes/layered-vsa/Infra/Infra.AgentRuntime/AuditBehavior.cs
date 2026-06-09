namespace Infra.AgentRuntime;

public sealed class AuditBehavior : IPipelineBehavior
{
    public async Task<TResponse> Run<TRequest, TResponse>(TRequest request, Func<Task<TResponse>> next, CancellationToken ct)
    {
        Console.WriteLine($"[audit] -> {typeof(TRequest).Name}");
        var response = await next();
        Console.WriteLine($"[audit] <- {typeof(TResponse).Name}");
        return response;
    }
}
