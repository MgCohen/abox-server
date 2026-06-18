using FastEndpoints;
using ABox.Domain.Decisions;
using ABox.Features.Decisions.Contracts;

namespace ABox.Features.Decisions.Answer;

internal sealed class AnswerDecisionEndpoint(IDecisions decisions) : Endpoint<AnswerDecisionRequest, DecisionView>
{
    public override void Configure()
    {
        Post("/decisions/{id}/answer");
        AllowAnonymous();
    }

    public override async Task HandleAsync(AnswerDecisionRequest req, CancellationToken ct)
    {
        if (await decisions.Answer(Route<Guid>("id"), req.Answer, req.Note, ct) is not { } d)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(
            new DecisionView(d.Id, d.Question, d.Tags, d.CreatedAt, d.Answer, d.Note, d.AnsweredAt), ct);
    }
}
