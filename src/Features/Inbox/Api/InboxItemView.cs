namespace ABox.Features.Inbox.Api;

public sealed record InboxItemView(
    Guid Id,
    string Title,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SeenAt,
    DateTimeOffset? CompletedAt);
