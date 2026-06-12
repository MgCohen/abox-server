namespace ABox.Domain.Flow;

public interface IFlowFactory
{
    Flow Create(FlowDefinition definition);
}
