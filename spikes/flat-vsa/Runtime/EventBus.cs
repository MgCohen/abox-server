using Microsoft.Extensions.DependencyInjection;

namespace App.Runtime;

public interface IEventHandler<in TEvent>
{
    Task Handle(TEvent @event, CancellationToken ct);
}

public interface IEventBus
{
    Task Publish<TEvent>(TEvent @event, CancellationToken ct = default);
}

public sealed class InProcessEventBus(IServiceProvider services) : IEventBus
{
    public async Task Publish<TEvent>(TEvent @event, CancellationToken ct = default)
    {
        foreach (var handler in services.GetServices<IEventHandler<TEvent>>())
            await handler.Handle(@event, ct);
    }
}
