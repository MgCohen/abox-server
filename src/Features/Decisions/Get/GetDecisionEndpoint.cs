using FastEndpoints;
using ABox.Domain.Decisions;
using ABox.Features.Decisions.Contracts;

namespace ABox.Features.Decisions.Get;

internal sealed class GetDecisionEndpoint(IDecisions decisions) : EndpointWithoutRequest<DecisionView>
{
    public override void Configure()
    {
        Get("/decisions/{id}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (await decisions.Get(Route<Guid>("id"), ct) is not { } d)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(
            new DecisionView(d.Id, d.Question, d.Tags, d.CreatedAt, d.Answer, d.Note, d.AnsweredAt), ct);
    }
}
