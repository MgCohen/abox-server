namespace Flows.Contracts;

public sealed record RunFlowRequest(string Project);

public sealed record RunFlowResponse(Guid FlowId, string Status);
