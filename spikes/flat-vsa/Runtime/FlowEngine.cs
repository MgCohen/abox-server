using App.Domain;

namespace App.Runtime;

public interface IFlowEngine
{
    Flow Launch(string project);
    Flow? Find(Guid id);
}

public sealed class StubFlowEngine : IFlowEngine
{
    private readonly Dictionary<Guid, Flow> _flows = new();

    public Flow Launch(string project)
    {
        var flow = Flow.Launch(project).Complete();
        _flows[flow.Id] = flow;
        return flow;
    }

    public Flow? Find(Guid id) => _flows.GetValueOrDefault(id);
}
