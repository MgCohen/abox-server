using FastEndpoints;
using ABox.Domain.Decisions;
using ABox.Features.Decisions.Contracts;
using ABox.Features.Decisions.Get;

namespace ABox.Features.Decisions.Raise;

internal sealed class RaiseDecisionEndpoint(IDecisions decisions) : Endpoint<RaiseDecisionRequest, DecisionView>
{
    public override void Configure()
    {
        Post("/decisions");
        AllowAnonymous();
    }

    public override async Task HandleAsync(RaiseDecisionRequest req, CancellationToken ct)
    {
        var question = req.Question?.Trim() ?? string.Empty;
        if (question.Length == 0)
        {
            AddError(r => r.Question, "A decision needs a question.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var decision = await decisions.Raise(question, req.Tags ?? [], ct);
        await Send.CreatedAtAsync<GetDecisionEndpoint>(
            new { id = decision.Id },
            new DecisionView(
                decision.Id, decision.Question, decision.Tags, decision.CreatedAt,
                decision.Answer, decision.Note, decision.AnsweredAt),
            cancellation: ct);
    }
}
