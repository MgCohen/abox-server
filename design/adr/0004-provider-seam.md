# ADR 0004 — The provider seam: one Agent, typed providers, provider-owned normalization

- **Status:** Accepted (2026-06-02).
- **Scope:** the rebuild (`/src`), the `Agents` actor family. Applies going forward; the L6
  agent baseline is the first to align.
- **Refines:** [ADR 0003](0003-actors-operations-run-contract.md) §1 + §Consequences — the
  agent **drive lifecycle** moves out of an abstract `Agent` base into a **composed
  `IProvider`**. ADR 0003's "an abstract base where implementations genuinely share (`Agent`,
  for the provider drive lifecycle)" is retired here; everything else in 0003 stands.

## Context

ADR 0003 left the agent as an actor that minted operations, sharing a **provider drive
lifecycle** via an abstract base — i.e. typed agents (`ClaudeAgent`, `CodexAgent`) overriding a
`DriveAsync` (Template Method). Building L6 surfaced two faults:

1. **The drive seam wore an inheritance costume.** The shared `RunOperation` lived in the base
   and had to **reach back into the agent's abstract `DriveAsync`** to do the one thing that
   varied. The back-reference was the signal that the variation point wanted to be a
   collaborator, not a subclass hook.
2. **Normalization had no home.** Claude and Codex differ on **two** axes — how you *drive*
   them (ConPTY isatty vs. subprocess `exec`; oracle A2) and how they *speak* (Claude JSONL vs.
   Codex JSON streams; oracle A6/A9) — yet every consumer expects **one** shape
   (`AgentResult` + transcript). Nothing in the 0003 model owned the translation.

## Decision

### 1. The polymorphism axis is the provider, not the agent

One **concrete** `Agent`; the variation lives in `IProvider` (`FakeProvider`, later
`CodexProvider`, `ClaudeProvider`). There is **no `ClaudeAgent`/`CodexAgent`.** This diverges
from the prototype's typed agents on purpose — the prototype is a reference, not the authority.
The named-agent identity (implementer / reviewer) is **config**, never a type.

### 2. Drive by composition, not inheritance

`Agent` is sealed/concrete and holds an `IProvider`. `agent.Run(prompt)` mints a
**self-contained** operation that carries the provider + the per-call inputs — it does not reach
back into the agent.

```csharp
public interface IProvider
{
    Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct);
}
```

### 3. The provider owns the full round-trip — including normalization

A provider's responsibility is to **speak one CLI's dialect end to end**: build args, drive the
substrate, **and parse/normalize that CLI's wire format into the uniform `DriveResult`.**

Normalization **cannot** be a peer the agent orchestrates. If the agent took raw output and then
chose a parser, it would have to know "this is a Claude provider → use the Claude parser" —
re-coupling the agent to provider types and resurrecting the typed agent §1 just killed. So
parse lives **with** the provider.

```
CodexProvider  = args + SubprocessSession (drive) + CodexJsonl/SessionId (parse) -> DriveResult
ClaudeProvider = args + PtySession        (drive) + ClaudeJsonl          (parse) -> DriveResult
```

### 4. The parser is a pure, fixture-tested internal unit

Inside each provider, parsing is a dedicated **pure function** — `raw output -> (Text,
SessionId, ExitCode, Transcript)` — tested against **recorded fixtures** with no process spawn.
This is where the oracle's hard-won bits (Claude JSONL path/schema A6, Codex session-id sniff /
`-o` read A9) port — into the **provider's parser**, not into agent subclasses.

### 5. The uniform contract is the normalization target

`DriveResult` + `AgentTurn` / `AgentTurnKind` (Text / Thinking / ToolUse / ToolResult) is the
shape **every** provider must produce. The agent **never sees raw output.** `DriveResult`
(provider side) maps 1:1 to `AgentResult` (consumer side); the two types mark the
provider↔agent boundary and may diverge later. Identical today.

### 6. Config selects the provider; the provider owns its config

The factory dispatches on **config subtype** (`FakeAgentConfig -> FakeProvider`, later
`CodexConfig -> CodexProvider`), wrapping the provider in the uniform `Agent`. The provider
**holds its config** (Model, SystemPrompt, and provider-specific fields like Codex `Sandbox`),
so `AgentRunRequest` carries only **per-call** data (`Prompt`, `ProjectDir`, `SessionId`).
Provider-specific fields never belong in the generic request.

The provider also owns the **substrate** choice — `SubprocessSession` for Codex, ConPTY
`PtySession` for Claude (A2) — and **subscription safety** (Claude env-scrub + isatty) as
provider-internal policy, never a standalone Tool/Step.

## Consequences

- `Agent` is one concrete class. Provider growth = a new `IProvider` impl + one factory arm + a
  fixture-tested parser. **Codex and Claude land as pure adds, with no `Agent` change.**
- `FakeProvider` replaces `FakeAgent`; tests drive through `IProvider` doubles
  (`CapturingProvider`).
- The drive lifecycle leaves the agent; ADR 0003's abstract-base drive seam is retired in
  favour of the composed seam.

## Watch-point — when a typed agent would return

One `Agent` holds **only while provider differences stay confined to args + drive + parse.** The
case that resurrects a typed agent (or, better, an injected **agent-level strategy**) is
provider-specific **orchestration** that can't be expressed as a uniform loop over normalized
events — e.g. an interaction loop that isn't just "drive → handle normalized question events →
continue." The plan to avoid it: providers normalize even interaction events into the uniform
stream, so the agent's loop stays provider-agnostic. Introduce the strategy **then**, on the
second real need — not pre-emptively.

## Alternatives considered

- **Typed agents overriding a drive method** (ADR 0003's shape) — the shared operation must
  reach back into an abstract method, and parsing has no home distinct from drive. The
  back-reference *was* the smell. Rejected.
- **Agent orchestrates a separate parser/normalizer** — forces the agent to match raw output to
  a parser by provider type, re-coupling agent↔provider. Rejected.
- **Raw `DriveResult` passed up, parsed by the agent/operation** — same coupling, and leaks the
  wire format past the provider boundary. Rejected.
- **Defer the provider seam to Codex** (the L6 plan-of-record) — reversed: the inheritance
  placeholder was confusing in-hand, and the second drive (Codex) is imminent, so the seam
  earns its keep now rather than after.
