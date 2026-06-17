using ABox.Infrastructure.Storage;

namespace ABox.Domain.Inbox;

// The inbox surface over the shared JsonRepository. Reading an item is what marks it seen — the item
// lifecycle is driven here, by the system, never by an outside caller (MarkSeen/Complete are internal).
public sealed class Inbox(IRepository<InboxItem> items) : IInbox
{
    public Task Add(InboxItem item, CancellationToken ct = default) => items.Add(item, ct);

    public async Task<InboxItem?> Get(Guid id, CancellationToken ct = default)
    {
        if (await items.GetById(id, ct) is not { } item) return null;
        var seen = item.MarkSeen();
        if (!ReferenceEquals(seen, item)) await items.Update(seen, ct);
        return seen;
    }

    public async Task<IReadOnlyList<InboxItem>> Query(IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        var all = await items.GetAll(ct);
        return [.. all
            .Where(item => tags.Count == 0 || tags.All(item.Tags.Contains))
            .OrderBy(item => item.CreatedAt)];
    }

    public async Task<InboxItem?> Complete(Guid id, CancellationToken ct = default)
    {
        if (await items.GetById(id, ct) is not { } item) return null;
        var done = item.Complete();
        if (!ReferenceEquals(done, item)) await items.Update(done, ct);
        return done;
    }
}
