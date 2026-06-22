# CLAUDE.md

Index for agents working in this repo. Keep it short — it routes to the
canonical docs rather than restating them.

## Talking to the owner

- Direct and instructional. No prose padding, no preamble/postamble.
- Prefer tables and diagrams over paragraphs.
- Don't close turns with CI / policy-guard / `send_later` / PR-watching
  boilerplate. Raise those only when asked or when actively watching a PR.

## Operating conventions

How we operate travels with the portable engine under `governance/harness/`. These
imports are auto-loaded; edit them there, not here:

@governance/harness/conventions/code-standards.md
@governance/harness/conventions/agent-guardrails.md
@governance/harness/conventions/test-rulebook.md

## What we're doing

Re-authoring the **spine** of a .NET 10 Unity-agent orchestrator (Host + Blazor
UI + library that drives `claude`/`codex` CLIs over ConPTY for subscription
billing). This is a **rebuild of internals, not behavior**: if a user can't tell
the difference in what the system *does*, the rebuild succeeded. We build in
**12 layers (L1→L12)**, walking-skeleton-first.

> **The Blazor UI lives in a separate client repo — it is NOT rebuilt here.** This
> repo is the **server/API** the existing client consumes. Don't scaffold a UI here;
> wire contracts in `*.Contracts` are the seam the client binds. (The `prototype/ui/`
> Blazor is reference-only, like the rest of `prototype/`.)

Source of truth, in order:

- **Constitution (behavior):** [`behavioral-oracle.md`](governance/design/behavioral-oracle.md)
  — Tier A invariants you MUST honor; Tier B prototype notes you must NOT follow
  unless we make a fresh, explicit decision. Cite the Tier-A item when you rely on it.
- **Specs + plan:** [`plans/rebuild/`](governance/plans/rebuild) — `01-feature-map.md`
  (capabilities, WHAT/WHY), `02-prd.md` (EARS requirements + R-SPINE/R-ARCH
  rules), `03-implementation-plan.md` (layer architecture + L1→L12 build order).
  The plan's "Current state" + done-when gates are authoritative for progress.
- **Decisions (ADRs):** [`decisions/`](governance/decisions) — focused records for choices
  that outlive a single layer. `0001` fixes the flow catalog / config / context model.

## `prototype/` is a REFERENCE, not source of truth

The original code is quarantined under [`prototype/`](prototype) (still builds &
runs). It is a **spike / behavioral reference only.** Behavior is locked by the
oracle and specs — `prototype/` is merely *how the prototype happened to do it*,
which is exactly what we're re-authoring.

- **Never treat `prototype/` code as authoritative.** Don't copy-paste from it.
- **Port the few hard-won bits deliberately**, citing the oracle item (PTY/ConPTY
  choreography, Claude JSONL path/schema, subscription key-scrub, anti-zombie
  teardown). Copy with intent — don't drag the surrounding ergonomics along.
- Deleted at **L12** once parity (PRD AC1–AC6) is reached.

## The rebuild lives in `/src` + `/tests`

One unified solution: `ABox.slnx`. Projects: `Core` (generic infra) ←
`ABox` (the orchestrator — `Agents/`, `Steps/`, `Flows/` as folders, not
separate assemblies); `Hosting` + `Host` compose; `Contracts` holds shared wire
DTOs. An assembly boundary exists only where it earns enforcement or reuse — see
`governance/plans/rebuild/03-implementation-plan.md` § Assembly layout.

**Build & test:**
```
dotnet build ABox.slnx
dotnet test  ABox.slnx
```

The test system's front door is [`tests/README.md`](tests/README.md).
