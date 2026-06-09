using Flows.Contracts;
using Infra.AgentRuntime;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using Notifications.ListNotifications;

namespace Notifications;

public static class NotificationsFeature
{
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        services.AddSingleton<NotificationStore>();
        services.AddTransient<IApiHandler<ListNotificationsRequest, IReadOnlyList<NotificationDto>>, ListNotificationsHandler>();
        services.AddTransient<IEventHandler<FlowCompleted>, FlowCompletedSubscriber>();
        return services;
    }
}
