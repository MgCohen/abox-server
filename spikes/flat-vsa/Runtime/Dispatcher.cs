using Microsoft.Extensions.DependencyInjection;

namespace App.Runtime;

public sealed class Dispatcher(IServiceProvider services, IEnumerable<IPipelineBehavior> behaviors)
{
    public Task<TResponse> Send<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
    {
        var handler = services.GetRequiredService<IApiHandler<TRequest, TResponse>>();
        Func<Task<TResponse>> next = () => handler.Handle(request, ct);
        foreach (var behavior in behaviors.Reverse())
        {
            var inner = next;
            next = () => behavior.Run<TRequest, TResponse>(request, inner, ct);
        }
        return next();
    }
}
