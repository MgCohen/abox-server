using System.Text.Json.Serialization;
using ABox.Infrastructure.Storage;

namespace ABox.Domain.Inbox;

[JsonPolymorphic]
[JsonDerivedType(typeof(NoteInboxItem), "note")]
public abstract record InboxItem : IEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Title { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SeenAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }

    internal InboxItem MarkSeen() => SeenAt is null ? this with { SeenAt = DateTimeOffset.UtcNow } : this;

    internal InboxItem Complete() => CompletedAt is null ? this with { CompletedAt = DateTimeOffset.UtcNow } : this;
}
