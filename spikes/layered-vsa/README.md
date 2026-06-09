# layered-vsa — assembly-per-vertical layout

Every vertical is **two assemblies** (a Contracts leaf + a Feature impl), sitting
on a shared Domain and a two-stratum Infra. The project-reference graph IS the
architecture — boundaries are enforced by the **compiler**, not convention.

```
layered-vsa/
  Domain/                              ← Domain.csproj      (shared aggregates, zero deps)
  Infra/
    Infra.Platform/                    ← generic plumbing (ISystemClock), zero deps
    Infra.AgentRuntime/                ← the moat: dispatcher, pipeline, bus, engine
                                          -> Domain, Infra.Platform
  Features/
    Flows/
      Flows.Contracts/                 ← leaf: requests, DTOs, FlowCompleted  (zero deps)
        RunFlow.cs  GetFlowSnapshot.cs  FlowEvents.cs
      Flows.Feature/                   ← internal handlers; one public AddFlows()
        RunFlow/RunFlowHandler.cs              -> Flows.Contracts, Domain,
        GetFlowSnapshot/GetFlowSnapshotHandler.cs   Infra.AgentRuntime
        FlowsFeature.cs
    Notifications/
      Notifications.Contracts/         ← leaf
      Notifications.Feature/           ← -> Notifications.Contracts, Flows.Contracts,
        ListNotifications/...                Domain, Infra.AgentRuntime
        FlowCompletedSubscriber.cs
        NotificationStore.cs (internal)
        NotificationsFeature.cs
  Host/                                ← composition root -> both Features + Runtime
  ArchCheck/                           ← reflection wall-test (second ring)
```

## Dependency graph (project references)

```
Domain                     (no deps)
Infra.Platform             (no deps)
Flows.Contracts            (leaf — zero deps)
Notifications.Contracts    (leaf — zero deps)
Infra.AgentRuntime         -> Domain, Infra.Platform
Flows.Feature              -> Flows.Contracts, Domain, Infra.AgentRuntime
Notifications.Feature      -> Notifications.Contracts, Flows.Contracts, Domain, Infra.AgentRuntime
Host                       -> Flows.Feature, Notifications.Feature, Infra.AgentRuntime
ArchCheck                  -> Flows.Feature, Notifications.Feature
```

## What this layout buys

- **Compiler walls.** `Flows.Feature` has no reference to `Notifications.Feature`,
  so a sideways reach is a **CS0246 compile error** — not a lint finding. The
  inner-loop gate an agent can't skip.
- **Real encapsulation.** Handlers and `NotificationStore` are `internal`; the
  only public surface per vertical is its `AddX()` extension. Other assemblies
  physically cannot see the internals.
- **Contracts as leaves.** A UI/WASM client references `*.Contracts` only — zero
  risk of dragging the engine into the client bundle. Sideways reaction
  (`FlowCompletedSubscriber`) goes through `Flows.Contracts`, never `Flows.Feature`.

## What it costs

- **Project sprawl.** Two assemblies per vertical + Domain + 2 infra + Host +
  ArchCheck. This 2-feature spike is already 9 projects.
- **Ceremony per use case.** Request/DTO live in Contracts, handler in Feature —
  a slice spans two projects and two folders.
- **More csproj wiring** every time a vertical needs a new reference.

This is the "enforce with the compiler, pay in project count" end of the spectrum.
