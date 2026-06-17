namespace ABox.Domain.Inbox;

public interface IInbox
{
    Task Add(InboxItem item, CancellationToken ct = default);

    Task<InboxItem?> Get(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<InboxItem>> Query(IReadOnlyList<string> tags, CancellationToken ct = default);

    Task<InboxItem?> MarkSeen(Guid id, CancellationToken ct = default);

    Task<InboxItem?> Complete(Guid id, CancellationToken ct = default);
}
