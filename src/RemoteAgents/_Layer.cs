// RemoteAgents — the orchestrator. Internal layout is folders/namespaces, not
// assemblies:
//
//   Agents/  provider framework + concrete Claude/Codex + roles + terminals + hooks (L5–L7, L9, L11)
//   Steps/   Step<T>, StepContext (internal ctor), Flow.Run<T> entry, concrete steps (L3, L8)
//   Flows/   Flow framework + the four recipes (L2 tech, L10 recipes)
//
// R-SPINE-1 (tools only run inside a Step) is enforced by API SHAPE, not a
// compile wall: Flow.Run<T>(Step<T>) does the lifecycle bookkeeping once, and
// agent/validator invocation is reachable only via StepContext, whose ctor is
// internal to this assembly. A deliberate bypass is reviewable — add a Roslyn
// analyzer if one ever actually appears. Intentionally near-empty at L1.
