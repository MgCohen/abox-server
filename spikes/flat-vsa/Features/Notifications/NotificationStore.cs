using App.Domain;

namespace App.Features.Notifications;

public sealed class NotificationStore
{
    private readonly List<Notification> _items = new();

    public void Add(string message) => _items.Add(new Notification(Guid.NewGuid(), message));

    public IReadOnlyList<Notification> All() => _items;
}
