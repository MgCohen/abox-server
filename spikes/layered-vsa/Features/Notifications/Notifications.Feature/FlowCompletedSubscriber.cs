using Flows.Contracts;
using Infra.AgentRuntime;

namespace Notifications;

internal sealed class FlowCompletedSubscriber(NotificationStore store) : IEventHandler<FlowCompleted>
{
    public Task Handle(FlowCompleted @event, CancellationToken ct)
    {
        store.Add($"Flow {@event.FlowId} finished for {@event.Project}");
        return Task.CompletedTask;
    }
}
