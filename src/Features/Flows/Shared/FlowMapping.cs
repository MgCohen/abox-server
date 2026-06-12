using ABox.Domain.Flow;
using ABox.Features.Flows.Contracts;

namespace ABox.Features.Flows.Shared;

public static class FlowMapping
{
    public static FlowView ToView(this FlowSnapshot s) =>
        new(s.Id, s.Flow, s.Project, s.Phase.ToString(), s.Version, s.CreatedAt,
            [.. s.Operations.Select(o => new FlowOperationView(
                o.Name, o.Status.ToString(), o.StartedAt, o.EndedAt, o.Summary, o.Error))],
            [.. s.Decisions.Select(d => new FlowDecisionView(
                d.Kind, d.Prompt, d.Answer, d.Source, d.At))]);
}
