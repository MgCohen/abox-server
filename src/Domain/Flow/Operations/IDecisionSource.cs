namespace ABox.Domain.Flow.Operations;

// An operation that resolved decisions during its run exposes them here; Flow.Run
// drains them onto the run ledger after the operation completes.
public interface IDecisionSource
{
    IReadOnlyList<DecisionDto> Decisions { get; }
}
