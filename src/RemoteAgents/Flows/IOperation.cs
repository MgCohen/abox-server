namespace RemoteAgents.Flows;

public interface IOperation<T>
{
    string Name { get; }

    Task<T> Execute(FlowContext ctx, CancellationToken ct);
}
