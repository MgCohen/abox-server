namespace RemoteAgents.Flows;

public interface IStepHandler<T>
{
    string Name { get; }

    Task<T> RunAsync(FlowContext ctx, CancellationToken ct);
}
