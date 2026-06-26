using ABox.Infrastructure.Storage;

namespace ABox.Domain.Decisions;

public sealed record Decision : IEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Question { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool? Answer { get; init; }
    public string? Note { get; init; }
    public DateTimeOffset? AnsweredAt { get; init; }

    internal Decision Resolve(bool answer, string? note) =>
        AnsweredAt is null
            ? this with { Answer = answer, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(), AnsweredAt = DateTimeOffset.UtcNow }
            : this;
}
