namespace ABox.Domain.Inbox;

public interface IInbox
{
    void Add(InboxItem item);

    InboxItem? Get(Guid id);

    IReadOnlyList<InboxItem> Query(IReadOnlyList<string> tags);
}
