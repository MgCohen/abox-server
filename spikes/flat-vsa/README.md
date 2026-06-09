# flat-vsa — single-project layout

One assembly. Verticality is expressed by **folders**, not projects. Each use
case is a single self-contained file (request + response + handler co-located).

```
flat-vsa/
  App.csproj                       ← the ONE project
  Program.cs                       ← composition root + demo
  Domain/                          ← shared aggregates (Flow, FlowPhase, Notification)
  Runtime/                         ← dispatcher, forced pipeline, event bus, flow engine
  Features/
    Flows/
      RunFlow.cs                   ← request + response + handler, one file
      GetFlowSnapshot.cs           ← request + response + DTO + mapping handler
      FlowEvents.cs                ← FlowCompleted
      FlowsRegistration.cs         ← AddFlows()
    Notifications/
      ListNotifications.cs
      FlowCompletedSubscriber.cs   ← reacts to Flows' FlowCompleted
      NotificationStore.cs
      NotificationsRegistration.cs
```

## What this layout buys

- **Fewest moving parts.** One csproj, one build, trivial to read top-to-bottom.
- **Maximum co-location.** A use case is one file; nothing is spread across
  three assemblies.
- **Cheap to refactor.** Move a file between feature folders — no project edits.

## What it gives up

- **No compiler walls.** `FlowCompletedSubscriber` does `using App.Features.Flows;`
  and can touch **anything** in Flows — the handler, the engine, internal state.
  Nothing is `internal` across features because it's all one assembly.
- **Boundaries are convention only.** The only thing stopping a sideways reach is
  a folder name and discipline. An agent that drifts won't hit a compile error.
- **ArchTests are the *only* enforcement** — reflection/namespace rules, which an
  agent can edit or delete in the same PR that breaks them.

This is the "trust the slice, enforce with tests" end of the spectrum.
