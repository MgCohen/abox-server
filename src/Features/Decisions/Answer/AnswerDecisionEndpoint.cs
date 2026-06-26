using FastEndpoints;
using ABox.Domain.Decisions;
using ABox.Features.Decisions.Contract;

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
        if (req.Answer is not { } answer)
        {
            AddError(r => r.Answer, "An answer must be yes or no.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        if (await decisions.Answer(Route<Guid>("id"), answer, req.Note, ct) is not { } d)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(
            new DecisionView(d.Id, d.Question, d.Tags, d.CreatedAt, d.Answer, d.Note, d.AnsweredAt), ct);
    }
}
