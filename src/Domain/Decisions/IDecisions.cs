namespace ABox.Domain.Decisions;

public interface IDecisions
{
    Task<Decision> Raise(string question, IReadOnlyList<string> tags, CancellationToken ct = default);

    Task<IReadOnlyList<Decision>> List(CancellationToken ct = default);

    Task<Decision?> Get(Guid id, CancellationToken ct = default);

    Task<Decision?> Answer(Guid id, bool answer, string? note, CancellationToken ct = default);
}
