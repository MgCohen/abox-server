# remote-agents-dotnet

C#/.NET 10 rewrite of the JS orchestrator at `../remote-agents/orchestrator/`.

See [`../PLANS/csharp-orchestrator-build.md`](../PLANS/csharp-orchestrator-build.md)
for the build plan and [`../PLANS/csharp-orchestrator-prd.md`](../PLANS/csharp-orchestrator-prd.md)
for the PRD.

## Solution

- `RemoteAgents/` — library (net10.0).
- `RemoteAgents.Tests/` — xUnit tests.
- `flows/` — `.NET 10` file-based programs that compose agents + primitives.
- `agents/` — named agent factories with sidecar prompts.
- `validation/` — per-project `IValidator` implementations.
- `bin/` — CLI shim (`agents-dotnet`).
- `sessions/` — gitignored per-run session directories.

## Build

```pwsh
dotnet build
dotnet test
```

Windows-only in v1 (see Q12 in the build plan).

## Docs

- [`docs/usage.md`](docs/usage.md) — day-to-day commands, writing flows / validators / named agents.
- [`docs/architecture.md`](docs/architecture.md) — internals: PTY trick, layers, sessions, sinks, source-gen JSON.
