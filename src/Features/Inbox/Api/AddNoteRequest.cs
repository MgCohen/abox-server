namespace ABox.Features.Inbox.Api;

public sealed record AddNoteRequest(string? Title, IReadOnlyList<string>? Tags);
