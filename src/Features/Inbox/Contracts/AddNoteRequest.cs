namespace ABox.Features.Inbox.Contracts;

public sealed record AddNoteRequest(string? Title, IReadOnlyList<string>? Tags);
