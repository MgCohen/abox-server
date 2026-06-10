# Architecture tests

Reference-graph enforcement (ArchUnitNET). These tests fail the build when the
dependency rules of the structure migration are violated, so the clean DAG we
reached can't silently regress.

## Built to extend without rebuilding

- **`ArchitectureModel.cs`** is the single source of truth: it loads the
  production assemblies **once** and defines the **bands** (layers) by namespace
  prefix.
- **Rules target bands, not assemblies.** A new assembly that lands in an existing
  band (e.g. another `RemoteAgents.Features.*`) is covered by existing rules with
  no change.
- **Slice rules auto-extend.** `SliceTests` matches `RemoteAgents.Features.(*)`,
  so every current *and future* feature is checked for isolation automatically.

## How to extend

| Want to… | Do this |
|----------|---------|
| Add a production assembly | add a `ProjectReference` (csproj) + one `Assembly.Load("…")` in `ArchitectureModel` |
| Add a band / layer | add one `IObjectProvider<IType>` in `ArchitectureModel`, plus the rules that constrain it |
| Add a rule | add a `[Fact]` to the relevant `*Tests` class (or a new class) |

## Current bands & allowed edges (down-only)

| Band | may depend on |
|------|----------------|
| Contracts | (nothing internal) |
| Infrastructure | (nothing internal) |
| Domain.Flow | Infrastructure, Contracts |
| Domain.Agents | Domain.Flow, Infrastructure, Contracts |
| Features.* | Domains, Infrastructure, Contracts (never a sibling feature) |
| Host | everything (composition root; referenced by nothing) |

`Domain.Agents → Domain.Flow` is allowed by design — they are two connected
domain models (an Agent *is* an Operation), not a forbidden domain-peer edge.

## Follow-ups (not yet enforced)

- `Web → Contracts only` — needs the Blazor WASM assembly loaded into the model.
- `PtySession` internal to `Domain.Agents` (the spawn wall) — add once it's internalized.
