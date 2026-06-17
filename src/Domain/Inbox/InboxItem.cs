namespace ABox.Domain.Inbox;

public abstract class InboxItem
{
    protected InboxItem(string title, IReadOnlyList<string> tags)
    {
        Id = Guid.NewGuid();
        Title = title;
        Tags = tags;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; }
    public string Title { get; }
    public IReadOnlyList<string> Tags { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? SeenAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public void MarkSeen() => SeenAt ??= DateTimeOffset.UtcNow;

    public void Complete() => CompletedAt ??= DateTimeOffset.UtcNow;
}
