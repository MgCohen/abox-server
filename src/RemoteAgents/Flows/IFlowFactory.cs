namespace RemoteAgents.Flows;

public interface IFlowFactory
{
    Flow Create(FlowDefinition definition);
}
