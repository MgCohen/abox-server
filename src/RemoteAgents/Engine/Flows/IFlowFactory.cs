namespace RemoteAgents.Engine.Flows;

public interface IFlowFactory
{
    Flow Create(FlowDefinition definition);
}
