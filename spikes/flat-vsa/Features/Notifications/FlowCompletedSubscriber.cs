using App.Features.Flows;
using App.Runtime;

namespace App.Features.Notifications;

public sealed class FlowCompletedSubscriber(NotificationStore store) : IEventHandler<FlowCompleted>
{
    public Task Handle(FlowCompleted @event, CancellationToken ct)
    {
        store.Add($"Flow {@event.FlowId} finished for {@event.Project}");
        return Task.CompletedTask;
    }
}
