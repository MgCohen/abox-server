// RemoteAgents.Steps — the Step framework (L3) and concrete step types (L8).
// Intentionally empty at L1. This is the only assembly that can reach the
// internal agent-driving surface in RemoteAgents.Agents; all tool invocation is
// reachable only from inside a Step. Filled at L3 (Step<T>, StepContext, Run<T>,
// the IAgentInvocation seam) onward.
