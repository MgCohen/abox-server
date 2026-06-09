using Infra.AgentRuntime;
using Notifications.Contracts;

namespace Notifications.ListNotifications;

internal sealed class ListNotificationsHandler(NotificationStore store)
    : IApiHandler<ListNotificationsRequest, IReadOnlyList<NotificationDto>>
{
    public Task<IReadOnlyList<NotificationDto>> Handle(ListNotificationsRequest request, CancellationToken ct)
    {
        IReadOnlyList<NotificationDto> dtos = store.All()
            .Select(n => new NotificationDto(n.Id, n.Message)).ToList();
        return Task.FromResult(dtos);
    }
}
