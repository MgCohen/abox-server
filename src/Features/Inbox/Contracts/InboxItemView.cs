namespace ABox.Features.Inbox.Contracts;

public sealed record InboxItemView(
    Guid Id,
    string Title,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SeenAt,
    DateTimeOffset? CompletedAt);
