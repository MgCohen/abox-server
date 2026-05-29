---
type: plan
status: draft
tags: [#architecture, #refactor, #non-goals, #rejected]
---

# Rejected options — explicit non-goals

> Each item is something that came up during planning and was
> **explicitly chosen against**. Each entry says *why*, so a future
> session reading this can tell the difference between "we didn't
> think of it" and "we decided no." If real pressure changes a
> decision, update the entry with a date and the new direction.

---

## R1 — Flow composition (flow-of-flows)

**Considered**: an `IFlow` that calls other `IFlow.RunAsync` to
chain orchestrations (e.g. "run `claude-only` then `unity-validate`
then `commit`").

**Decided**: NO.

**Why**: a flow is a single-shot, start-to-end unit. Composition is
out of scope. Decoration is allowed (linear wrap around a single
flow run), but a flow does not invoke another flow.

**How to apply**: `IFlow.RunAsync` does not take an `IFlow` parameter
and does not resolve other flows through `IFlowRegistry`. If
shared multi-step orchestration becomes needed later, it lives as a
new `IFlow` whose body contains the steps inline — not as a
"composite flow" abstraction.

---

## R2 — Provider auto-discovery via reflection / plugin assemblies

**Considered**: scan for types implementing `Agent` in loaded
assemblies, register automatically.

**Decided**: NO.

**Why**: the project has a fixed, small provider set (Claude, Codex,
maybe one more). Reflection-based discovery adds startup cost, makes
behavior depend on assembly load order, and hides what's actually
registered. The composition root naming providers explicitly is
clearer.

**How to apply**: providers are registered by name in
`services.AddRemoteAgents(...)`. Adding a new provider is an
explicit two-line code change.

---

## R3 — A flow DSL / YAML / JSON flow definition

**Considered**: define flows as data files (YAML/JSON) interpreted
at runtime — e.g. a "step" list, conditionals, retries.

**Decided**: NO.

**Why**: every existing flow is shorter as C# than it would be as
YAML, and the C# version gets typing, debugger support, refactoring
tools, and call-site search. A DSL adds an interpreter, a schema,
and a separate set of bugs without removing any C#.

**How to apply**: flows stay as `IFlow` classes in C#. If
flow-as-data is ever justified, it's a separate effort with its
own plan.

---

## R4 — Cross-machine agent execution

**Considered**: agent runs distributed across multiple machines
(e.g. Claude on the laptop, Codex on a VM, results merged).

**Decided**: NO — at least, not in this refactor.

**Why**: the project is local-first, single-host, subscription-billed.
Cross-machine execution is a different kind of system, not a
refactor target. The Host already has REST + SignalR; if we ever
want remote agents, we'd add an `IRemoteAgentTransport` then. Not
now.

**How to apply**: `IAgentFactory` returns local agents. There's no
`IRemoteAgent` interface, no agent transport layer.

---

## R5 — API-key (non-subscription) provider paths

**Considered**: a fallback that uses `ANTHROPIC_API_KEY` when the
subscription path is unavailable.

**Decided**: NO.

**Why**: the project's whole reason for being is subscription
billing (Claude Max + ChatGPT plans). API-key billing
defeats the purpose. The existing `SubscriptionGuard` refuses to
start if any API-key env is set; the env scrub blanks them in the
child env. Both defenses stay.

**How to apply**: no code path takes a `ApiKey` parameter. The
`*_API_KEY` env vars stay forcibly blanked at every boundary.

---

## R6 — Behavior changes during the refactor

**Considered**: opportunistic "while we're here" improvements
(retry counts, timeouts, prompt tweaks, model swaps).

**Decided**: NO.

**Why**: a structural refactor is hard enough to verify when the
output stays byte-identical. Behavior changes mixed in make
regression hunting ambiguous — was it the structure or the new
behavior?

**How to apply**: every per-layer doc's acceptance criteria
includes "smokes pass with byte-identical session outputs"
(modulo Layer 6's *additive* chat-event variants, which are
documented explicitly). Anything that would change observable
behavior gets its own plan after the refactor settles.

---

## R7 — A single `OrchestrationEvent` flat hierarchy

**Considered**: keep `AgentEvent` flat (no `FlowEvent` sibling) and
just rename `Phase.AgentName` to `Phase.Source` to acknowledge the
overload.

**Decided**: NO. Split into `AgentEvent` and `FlowEvent` siblings.

**Why**: the overload is a smell, not a naming problem. Sinks,
the UI, and consumers want to dispatch on "agent did something"
vs "flow narrated a step." A rename to `Source` would let the
overload continue without the comment apologizing for it. The
split is cheap and makes consumers exhaustively typed.

**How to apply**: see [`03-events-and-sinks.md`](03-events-and-sinks.md).

---

## R8 — Per-provider chat-event hierarchies

**Considered**: keep `ChatEvent` separate from `AgentEvent`, with
provider-specific variants under `ChatEvent` (Claude-shaped tool
calls vs Codex-shaped, etc.).

**Decided**: NO. Chat content joins `AgentEvent` as generic
variants (`AssistantText`, `UserText`, `Thinking`, `ToolUse`,
`ToolResult`, `SummaryNote`). Provider-specific extras go into
the `Detail` / `InputJson` / `Content` fields as opaque payload.

**Why**: every consumer wants a unified stream. The provider
asymmetries are small; what we need on the wire is a stable shape.
If a provider has a meaningful extra (e.g. Codex's
`StopPayloadInspector` sentinel), it surfaces as a new variant on
the shared hierarchy, not a parallel one.

---

## R9 — `Run` carries an arbitrary `Dictionary<string, string> ProviderMetadata`

**Considered**: put provider-specific data on `Run` (or `RunRecord`)
in an open dict instead of typed fields.

**Decided**: NO. Use a typed `ProviderSessionRef(string Provider, string Id)`
slot, populated by an explicit `AgentEvent.ProviderSessionAttached`
variant.

**Why**: open dicts erode contracts. The Host today already abuses
this with `Run.ClaudeSessionId`; replacing that with a stringly-keyed
dict trades one form of leakage for another. A typed slot keeps the
schema explicit.

**How to apply**: see [`01-contracts.md`](01-contracts.md) gap #4.

---

## R10 — Decorators as domain gates

**Considered**: implement domain preconditions (clean-tree check,
`--push` gate, validation requirement) as `IFlow` decorators —
`RequireCleanTreeFlow(IFlow inner) : IFlow`,
`RequirePushFlag(IFlow inner) : IFlow`, etc.

**Decided**: NO. Decorators are observation-only — cross-cutting
concerns like timing (`TimedFlow`), structured logging (`LoggedFlow`),
retry-once. Domain preconditions go inside the flow body.

**Why**: a flow decorator that gates its inner flow is a flow that
runs another flow conditionally. That's flow-of-flows composition
wearing a different name, and R1 already ruled it out. Domain
preconditions are part of *the flow's own contract* ("this flow
requires a clean tree to run"), not orthogonal infrastructure;
expressing them inline keeps the contract visible in one place.

**How to apply**: `RequireCleanTreeFlow : IFlow` does not exist.
A flow that needs a clean tree calls `GitChecks.EnsureCleanAsync(...)`
on its first line and returns `FlowResult(AbortedDirtyTree, ...)` on
failure. Decorators in `AddFlow<X>().DecoratedWith<Y>()` registrations
are restricted to cross-cutting wrappers — see [`04-flows.md`](04-flows.md).

**Earlier consideration (kept for history)**: a declarative decorator
config tree (`AddFlow<...>().With(retry: 1, timeout: 30m)`) was also
considered and rejected; it drifts toward a DSL (R3). Plain C#
decorators registered in DI cover every cross-cutting case we need.

---

## R11 — Plug-in style sinks via filesystem drop

**Considered**: drop a `SinkX.dll` into a `plugins/` folder, get it
loaded automatically.

**Decided**: NO. Sinks are registered in code via `AddSink<T>()`.

**Why**: same family of objection as R2. We have a known sink set;
auto-discovery hides what's actually emitting events. Adding a
sink is a one-line code change.

---

## R12 — Provider as a runtime string / `IProvider` interface

**Considered**: keep "provider" as a runtime concept — a string field
on `Agent` (`Provider => "claude"`), a `[Provider("claude")]`
attribute, or an `IProvider` interface bundling agent + installer +
options for polymorphic iteration.

**Decided**: NO. Provider is a *type* and an *organizational* concept,
not a runtime value.

**Why**: the class hierarchy already encodes provider identity.
`ClaudeAgent` IS the Claude provider; a parallel `"claude"` string
is duplicate state waiting to drift (rename the class, forget the
string). An `IProvider` interface would be a thin bundle with nothing
polymorphic to call — we never need "loop over all providers";
every call site names the one it wants. The composition root's
`UseClaude` / `UseCodex` extension methods are where the provider
identity surfaces in code, exactly once per provider.

**How to apply**:
- No `Agent.Provider` property, no `[Provider]` attribute.
- No `IProvider` interface, no `IEnumerable<IProvider>` iteration.
- `IHookInstaller<TAgent>` ties installer to agent at compile time.
- The string `"claude"` appears in code only in (a) `Providers/Claude/`
  namespace declarations, (b) the wire-side `JsonPolymorphic`
  discriminator on serialized events. Same for `"codex"`.
- If "list installed providers" is ever needed (e.g. `/providers`
  REST endpoint), publish a list of `ProviderInfo` records from the
  composition root at registration time — not a reflection sweep.

---

## R13 — `IConfiguration` / `appsettings.json` binding for agent options

**Considered**: bind `ClaudeAgentOptions` / `CodexAgentOptions` from
`IConfiguration` via `services.Configure<T>(config.GetSection("RemoteAgents:Claude"))`.
Lets per-environment overrides land in `appsettings.Production.json`
or env vars like `RemoteAgents__Claude__LaunchSettleMinWaitMs`.

**Decided**: NO. Options are records with defaults; overrides are
lambdas at registration.

**Why**: this project is local-first, single-host, subscription-billed.
There are no environments (staging, production, region) to layer
config across. The IConfiguration machinery costs a magic colon-path
string per knob, a binder package dependency, and an indirection from
"where does this knob live?" to "let me grep appsettings, env vars,
and the binder." It buys flexibility we will not use.

**How to apply**:
- `RemoteAgents.Hosting.csproj` does not reference
  `Microsoft.Extensions.Configuration.*`.
- `UseClaude(Action<ClaudeAgentOptions>? configure = null)` takes an
  optional lambda; defaults stay on the record.
- The one knob that genuinely differs per host (`OrchestratorRoot`)
  is read from a single env var via `OrchestratorPaths.Resolve()`,
  not via `IConfiguration`.

---

## R14 — `AgentPreset` as a string-keyed DI registry

**Considered**: register named-agent presets through DI:
`services.AddAgentPreset("planner", new AgentPreset(...))`, then look
up via `IAgentFactory.Create("planner", sink)`.

**Decided**: NO. Presets are polymorphic record *values*; the call
site passes the value, not a string.

**Why**: a registry adds two parallel string identities (the
registration key and the lookup arg) plus a third place where the name
"planner" appears (the preset's own `Name` field), plus a runtime
lookup that can fail at startup or at first call. None of that earns
its keep — the preset value is a perfectly good handle. The static
field `AgentPresets.Planner` is one place, typed, IDE-navigable,
compile-checked.

**How to apply**:
- `AgentPreset` is an abstract record with `Build(IServiceProvider, IEventSink)`.
- `ClaudePreset` / `CodexPreset` subtypes encode the provider.
- `AgentPresets.Planner = new ClaudePreset(...)` etc. — values, not
  registrations.
- No `services.AddAgentPreset(string, ...)` call exists.
- No `IAgentFactory.Create(string, ...)` overload exists. If
  `IAgentFactory` exists at all, it's a thin pass-through whose only
  signature is `Build(AgentPreset, IEventSink)`; in the default plan
  it doesn't exist and call sites use `preset.Build(sp, sink)` directly.

---

## How to add a new entry

1. Briefly describe the option (one sentence).
2. State the decision (YES — but if you reach that, it isn't
   rejected; it belongs in a layer doc — or NO).
3. State the reason — what would the alternative cost?
4. State how to apply — a concrete rule that catches drift.

If a rejection is later overturned, leave the entry, append a
dated "Reversed: <date>" line with the reason, and point at the
new layer doc that owns the change. Don't delete history.
