# Prototype — reference only (NOT source of truth)

This folder holds the **original prototype** of the Remote Unity Agents
orchestrator: the backend library (`remote-agents-dotnet/`) and UI (`ui/`),
moved here verbatim on 2026-05-31.

It is **a spike / behavioral reference, not the source of truth.** The rebuild
lives in [`/src`](../src) and is governed by:

- **Constitution:** [`design/behavioral-oracle.md`](../design/behavioral-oracle.md)
  — Tier A invariants (honor) vs Tier B prototype notes (do-not-follow).
- **Specs + plan:** [`PLANS/rebuild/`](../PLANS/rebuild) — feature map → PRD →
  12-layer implementation plan.

## Rules of engagement

- **Do not treat this code as authoritative.** Behavior is locked by the oracle
  and the specs; *this code is how the prototype happened to do it,* which is
  exactly what the rebuild is re-authoring.
- **Port deliberately, never lift wholesale.** A few internals are genuinely
  hard-won and meant to be ported close to verbatim — the PTY/ConPTY
  choreography (Tier A2/A4/A5/A7, B1/B2), the Claude JSONL path/schema (Tier A6),
  the subscription-key scrub (Tier A1/A3), anti-zombie teardown (Tier A10). When
  porting these, copy with intent and cite the oracle item — don't import the
  surrounding ergonomics along with them.
- **Still runnable.** Both projects keep their relative references, so the
  prototype builds and runs as a living reference for parity checks (PRD AC1).
  Operational scripts (e.g. `ui/scripts/install-host-service.ps1`) may contain
  absolute paths that need updating if you redeploy it from here.

## Disposition

Deleted at **L12 (cleanup)** once the rebuild reaches behavioral parity and the
acceptance criteria (PRD AC1–AC6) pass.
