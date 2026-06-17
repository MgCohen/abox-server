using System.Collections.Concurrent;

namespace ABox.Domain.Inbox;

// Provisional: in-memory stub of the inbox surface. A durable store lands later (the Box plan's B2);
// IInbox is the seam that swap stays behind.
public sealed class InMemoryInbox : IInbox
{
    private readonly ConcurrentDictionary<Guid, InboxItem> _items = new();

    public void Add(InboxItem item) => _items[item.Id] = item;

    public InboxItem? Get(Guid id) => _items.TryGetValue(id, out var item) ? item : null;

    public IReadOnlyList<InboxItem> Query(IReadOnlyList<string> tags) =>
        [.. _items.Values
            .Where(item => tags.Count == 0 || tags.All(item.Tags.Contains))
            .OrderBy(item => item.CreatedAt)];
}
