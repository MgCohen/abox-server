using App.Features.Flows;
using App.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace App.Features.Notifications;

public static class NotificationsRegistration
{
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        services.AddSingleton<NotificationStore>();
        services.AddTransient<IApiHandler<ListNotificationsRequest, IReadOnlyList<NotificationDto>>, ListNotificationsHandler>();
        services.AddTransient<IEventHandler<FlowCompleted>, FlowCompletedSubscriber>();
        return services;
    }
}
