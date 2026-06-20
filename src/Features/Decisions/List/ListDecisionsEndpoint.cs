using FastEndpoints;
using ABox.Domain.Decisions;
using ABox.Features.Decisions.Contracts;

namespace ABox.Features.Decisions.List;

internal sealed class ListDecisionsEndpoint(IDecisions decisions) : EndpointWithoutRequest<IReadOnlyList<DecisionView>>
{
    public override void Configure()
    {
        Get("/decisions");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var all = await decisions.List(ct);
        await Send.OkAsync(
            [.. all.Select(d => new DecisionView(d.Id, d.Question, d.Tags, d.CreatedAt, d.Answer, d.Note, d.AnsweredAt))],
            ct);
    }
}
