using ABox.Domain.Inbox;
using ABox.Infrastructure.Storage;

namespace ABox.Domain.Decisions;

public sealed class Decisions(IRepository<Decision> decisions, IInbox inbox) : IDecisions
{
    public async Task<Decision> Raise(string question, IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        var decision = new Decision { Question = question, Tags = tags };
        await decisions.Add(decision, ct);
        await inbox.Add(new NoteInboxItem { Id = decision.Id, Title = question, Tags = tags }, ct);
        return decision;
    }

    public async Task<IReadOnlyList<Decision>> List(CancellationToken ct = default)
    {
        var all = await decisions.GetAll(ct);
        return [.. all.OrderBy(d => d.CreatedAt).ThenBy(d => d.Id)];
    }

    public Task<Decision?> Get(Guid id, CancellationToken ct = default) => decisions.GetById(id, ct);

    public async Task<Decision?> Answer(Guid id, bool answer, string? note, CancellationToken ct = default)
    {
        if (await decisions.GetById(id, ct) is not { } decision) return null;
        var resolved = decision.Resolve(answer, note);
        if (!ReferenceEquals(resolved, decision))
        {
            await decisions.Update(resolved, ct);
            await inbox.Complete(id, ct);
        }
        return resolved;
    }
}
