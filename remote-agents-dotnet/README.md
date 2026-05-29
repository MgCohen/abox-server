# remote-agents-dotnet

Local C#/.NET 10 orchestrator that drives `claude` and `codex exec`
against your Unity projects under **subscription billing** (Claude Max,
ChatGPT Plus/Pro), not the per-token API. Originally a JS prototype
(removed after C# parity shipped); design notes from that era live in
`../research/`.

See [`../PLANS/csharp-orchestrator-build.md`](../PLANS/csharp-orchestrator-build.md)
for the build plan and [`../PLANS/csharp-orchestrator-prd.md`](../PLANS/csharp-orchestrator-prd.md)
for the PRD.

## Layout

```
remote-agents-dotnet/
  Directory.Build.props        # UseArtifactsOutput=true — all bin/obj redirect to artifacts/
  RemoteAgents.slnx            # solution
  src/
    RemoteAgents/              # the library (net10.0)
      Core/                    # abstractions + reusable primitives (no vendor coupling)
      Providers/               # adapters: Claude, Codex, Unity, Orchestrator
      Flows/                   # composed pipelines built on Core + Providers
    NamedAgents/               # persona agents (Planner / Documenter / Researcher) + live-editable prompts
  tests/RemoteAgents.Tests/    # xUnit
  cli/
    agents-dotnet.cs           # CLI shim
    flows/                     # file-based programs (`dotnet run cli/flows/<flow>.cs`)
  docs/                        # usage.md + architecture.md
  artifacts/                   # all build output (gitignored)
  sessions/                    # per-run session dirs (gitignored)
```

Dependency rule: **Core ← Providers ← Flows / NamedAgents.** Core knows nothing of providers; providers know nothing of flows.

## Build

```pwsh
dotnet build
dotnet test
```

Windows-only in v1 (see Q12 in the build plan).

## Sibling: UI

The mobile / web / desktop front-end lives in [`../ui/`](../ui/) as a
separate tree with its own solution. It depends on this library via
`ProjectReference`; this library has no knowledge of `ui/`. The two can
be iterated independently — building this solution does not touch the UI.

## Docs

- [`docs/usage.md`](docs/usage.md) — day-to-day commands, writing flows / validators / named agents.
- [`docs/architecture.md`](docs/architecture.md) — internals: PTY trick, layers, sessions, sinks, source-gen JSON.
