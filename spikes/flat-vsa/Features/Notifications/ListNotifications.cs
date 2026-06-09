using App.Runtime;

namespace App.Features.Notifications;

public sealed record ListNotificationsRequest;

public sealed record NotificationDto(Guid Id, string Message);

public sealed class ListNotificationsHandler(NotificationStore store)
    : IApiHandler<ListNotificationsRequest, IReadOnlyList<NotificationDto>>
{
    public Task<IReadOnlyList<NotificationDto>> Handle(ListNotificationsRequest request, CancellationToken ct)
    {
        IReadOnlyList<NotificationDto> dtos = store.All()
            .Select(n => new NotificationDto(n.Id, n.Message)).ToList();
        return Task.FromResult(dtos);
    }
}
