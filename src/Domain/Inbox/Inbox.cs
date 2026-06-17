using ABox.Infrastructure.Storage;

namespace ABox.Domain.Inbox;

public sealed class Inbox(IRepository<InboxItem> items) : IInbox
{
    public Task Add(InboxItem item, CancellationToken ct = default) => items.Add(item, ct);

    public Task<InboxItem?> Get(Guid id, CancellationToken ct = default) => items.GetById(id, ct);

    public async Task<IReadOnlyList<InboxItem>> Query(IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        var all = await items.GetAll(ct);
        return [.. all
            .Where(item => tags.Count == 0 || tags.All(tag => item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)];
    }

    public Task<InboxItem?> MarkSeen(Guid id, CancellationToken ct = default) => Stamp(id, item => item.MarkSeen(), ct);

    public Task<InboxItem?> Complete(Guid id, CancellationToken ct = default) => Stamp(id, item => item.Complete(), ct);

    private async Task<InboxItem?> Stamp(Guid id, Func<InboxItem, InboxItem> stamp, CancellationToken ct)
    {
        if (await items.GetById(id, ct) is not { } item) return null;
        var stamped = stamp(item);
        if (!ReferenceEquals(stamped, item)) await items.Update(stamped, ct);
        return stamped;
    }
}
