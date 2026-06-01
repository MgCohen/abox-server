// RemoteAgents — the engine. Everything that can run a flow WITHOUT a web host.
// Internal layout is folders/namespaces, not assemblies:
//
//   Paths/    orchestrator root + projects.json discovery
//   Projects/ ProjectRegistry (short name → absolute dir)
//   Agents/   provider framework + concrete Claude/Codex + roles + terminals + hooks (L5–L7, L9, L11)
//   Steps/    Step<T>, StepContext (internal ctor), Flow.Run<T> entry, concrete steps (L3, L8)
//   Flows/    Flow framework + the four recipes + flow runtime (FlowRegistry/
//             FlowCatalog/history) (L2 tech, L10 recipes)
//
// Depends only on Contracts — no ASP.NET, no DI container. The web adapter +
// composition root live in RemoteAgents.Host.
//
// R-SPINE-1 (tools only run inside a Step) is enforced by API SHAPE, not a
// compile wall: Flow.Run<T>(Step<T>) does the lifecycle bookkeeping once, and
// agent/validator invocation is reachable only via StepContext, whose ctor is
// internal to this assembly. A deliberate bypass is reviewable — add a Roslyn
// analyzer if one ever actually appears.
