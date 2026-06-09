namespace Notifications.Contracts;

public sealed record ListNotificationsRequest;

public sealed record NotificationDto(Guid Id, string Message);
