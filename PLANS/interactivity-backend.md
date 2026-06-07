---
type: plan
status: ready
tags: [#interactivity, #permission, #resolver, #audit, #backend]
---

# Interactivity backend — everything up to the UI

> **Purpose.** Finish the interactivity model on the backend: all resolver
> variants, a real pending-decision await mechanism, a decoupled LLM resolver,
> and durable decision audit — **stopping short of the UI**. The UI-facing edge
> (the fulfill endpoint + the human-facing inbox screen) is the only deferred
> part; everything it will call is built and exercised here via a scripted
> fulfiller.
>
> **Source-of-truth links.** Extends the deferred items in
> [`permission-interaction-model.md`](permission-interaction-model.md) §3/§8
> (which named the human resolver + audit as "deferred to UI" without decomposing
> them). The question model comes from
> [`structured-questions-spike.md`](structured-questions-spike.md). Slots
> alongside the rebuild plan's L8→L10 stretch
> ([`rebuild/03-implementation-plan.md`](rebuild/03-implementation-plan.md)) —
> the L10 flows wire these resolvers.

---

## 1. Why

The seam (`IDecisionResolver`) and the autonomous path are built and merged. The
gap is the rest of the resolution surface:

- **Wait-for-human is not real.** Interactive maps to `NonInteractiveResolver`,
  which returns `null` → surfaces `NeedsInput` and **stops**. Nothing blocks-and-awaits.
- **No LLM resolver** — no path that delegates a question to an agent to answer.
- **No first-class Deny** — denial is only incidental (null-terminal / Auto guardrail).
- **Audit is provisional** — `AutoResolver` records assumptions to an in-memory
  queue marked PROVISIONAL; nothing reaches the durable run record.

This plan closes the first three and the resolver-path half of the fourth (every
decision a resolver makes is durably recorded), leaving the UI and one audit
fast-follow (provider-internal permission verdicts — §13).

---

> **Status (built).** Steps 1–4 are implemented and merged on
> `feat/interactivity-backend`: the `Resolution` axis + `DecisionKind` seam, the
> `DenyResolver`, the durable audit ledger, and the pending-decision await +
> `InteractiveResolver`. **The LLM resolver (§8, build step 5) is deferred** —
> Auto already covers "proceed autonomously," and an LLM resolver only earns its
> place once we want a *separate* brain deciding; `Resolution.Llm` stays as a
> reserved enum value the factory rejects until then.

## 2. Locked decisions (from planning Q&A)

1. **Build the needed resolver variants** — Interactive (real await) + Deny now;
   **LLM deferred** (see status note). (`AutoResolver` + `NonInteractiveResolver`
   already existed.)
2. **Real await mechanism.** A pending-decision registry + a resolver that
   **blocks until answered or run-cancelled** (no timeout). Only the HTTP
   fulfill-endpoint + UI are deferred; a **scripted/test fulfiller** drives it now.
3. **LLM resolver (deferred) folds in `IAgentFactory` directly** — when built it's
   one thin `IDecisionResolver` that mints a sub-agent; no separate `IRationalizer`
   seam (that would be an abstraction on its first use).
4. **Audit is durable** — decisions are recorded on the run, surfaced in the
   snapshot, and persisted via `IFlowHistory`; the provisional in-memory queue is retired.
5. **DP1 — resolver selection becomes a config axis** (see §4).
6. **DP2 — the resolve seam carries decision kind** (see §5).

> **Deferred to a fast-follow (was DP3).** Auditing the *provider-internal*
> permission verdicts (`AutoPolicy.Evaluate` mid-`DriveAsync`) is a second audit
> channel through `DriveResult`. The resolver-path decisions below already give us
> "record on run + persist"; the provider-internal channel lands once the ledger
> exists and we can see whether we miss it. See §13.

---

## 3. The resolver set (behind `IDecisionResolver`)

| Resolver | Behavior | Caps? |
|---|---|---|
| `AutoResolver` *(exists)* | Self-answer "assume low-risk, state it, continue" | yes (loops) |
| `LlmResolver` *(deferred — §8)* | Mint a sub-agent to answer | yes (loops) |
| `DenyResolver` *(built)* | Always refuse — deny choice for a permission Ask; terminal for an open question | no (terminal) |
| `InteractiveResolver` *(new)* | Register a pending decision, **await** answer-or-cancel | no (human self-terminates) |
| `NonInteractiveResolver` *(exists)* | `null` → terminal `NeedsInput` (the no-resolver default) | no (terminal) |

**Cap rule (refined).** The auto-only cap applies to resolvers that *always
produce an answer* and could therefore loop forever — `Auto` and `Llm`. `Deny`
and `Human` self-terminate, so they're uncapped. `AgentConfig.ResolveCap`
(default 8) is unchanged; only the factory's "when to pass it" predicate changes.

**`Deny` vs `NonInteractive` — keep both.** They behave identically on an *open
question* (both → `null` terminal); they differ only on a permission `Choice`,
where `Deny` actively returns the deny option. Don't let a later "simplify" merge
them — the merge silently loses the permission-deny behavior.

---

## 4. DP1 — resolver selection (config axis)

The binary `Interactivity {Autonomous, Interactive}` can no longer express the
choice. Replace it with one axis on `AgentConfig`:

```
enum Resolution { Auto, Llm, Deny, Human }
```

- `Human` ⇒ interactive await (the person is in the loop).
- `Auto | Llm | Deny` ⇒ autonomous, no person.
- **Derived, not separate knobs:**
  - *cap* → applied for `Auto`/`Llm`, null for `Deny`/`Human`.
  - *directive strictness* (the L6 unattended addendum threshold) → `Human` =
    strict (lower ask bar); `Auto`/`Llm`/`Deny` = autonomous (genuine blockers /
    irreversible forks only).

**Retire `Interactivity`.** Map the old values during the change: `Interactive ⇒
Human`, `Autonomous ⇒ Auto` (the prior default). `AgentFactory` dispatches on
`Resolution` to pick the resolver + the cap predicate.

---

## 5. DP2 — the resolve seam carries decision kind

`ResolveAsync(AgentQuestion, ct)` can't tell a permission Ask from an open
question, but `DenyResolver` behaves differently for each and the audit ledger
records the distinction. Add the one field that earns its place now:

```
enum DecisionKind { Permission, Question }

Task<string?> ResolveAsync(AgentQuestion question, DecisionKind kind, CancellationToken ct);
```

`DecisionKind` distinguishes a permission Ask (a `Choice`) from an end-of-turn
agent question. **No `RunId`, no wrapper record yet** — cancellation already rides
the `CancellationToken` (`InteractiveResolver` registers on it), and the only
consumer that needs run identity is the deferred inbox. Introduce a
`DecisionRequest` record when `RunId` actually lands with the inbox (§13); a
wrapper holding one live field plus one deferred field is the speculative-container
smell we're avoiding.

---

## 6. Audit model

```
record DecisionDto(           // Contracts, parallel to OperationDto
    string Kind,               // "Permission" | "Question"
    string Prompt,
    string Answer,             // the answer / allow / deny text
    string Source,             // Resolution.ToString(): "Auto" | "Llm" | "Deny" | "Human"
    DateTimeOffset At);
```

`Kind`/`Source` are stringified at the boundary: `DecisionDto` lives in `Contracts`
(the leaf), which can't reference the `DecisionKind`/`Resolution` enums in the
orchestrator. The source label comes from `IDecisionResolver.Source` (a new
property — the resolver *is* the decider, so it declares its own `Resolution`).
`Rationale` was dropped for now: nothing produces it and the seam returns a single
string; it returns in §8 only if the LLM resolver separates answer from rationale.

- **Collected on the run.** `FlowContext` gains a decision ledger
  (`RecordDecision` + a read-only list), surfaced on `FlowSnapshot` and persisted
  with the snapshot via `IFlowHistory` (no new store).
- **Drained, not threaded.** The `Agent` accumulates its turn's decisions and
  exposes them via a capability seam, `IDecisionSource`; `Flow.Run` drains them
  onto the ledger after the operation completes. This keeps the `Agent` free of any
  `FlowContext` dependency (cleaner than the Agent holding the context directly).
- **Retire** `AutoResolver`'s in-memory `Assumptions` queue — its content becomes
  `DecisionDto{Source="Auto"}` on the run.

> Provider-internal permission verdicts (`AutoPolicy` mid-`DriveAsync`) are *not*
> captured here — that's the deferred fast-follow (§13). The resolver path above is
> the whole audit surface for the first cut.

---

## 7. Pending-decision backend (real) + fulfillment (scripted now)

- `PendingDecisions` registry *(new, singleton)*:
  - `Task<string?> Register(PendingDecision, CancellationToken ct)` — create a
    `TaskCompletionSource`, store it, register `ct` to complete it with `null` so
    `FlowRegistry.Cancel` (which trips the run's `ct`) unblocks the await (`null`
    ⇒ terminal). No `RunId` needed — the `ct` carries the cancel linkage.
  - `bool Resolve(Guid id, string answer)` — complete the TCS.
  - `IReadOnlyList<PendingDecision> List()` — the future inbox source; also lets
    the snapshot show "blocked on decision X".
- `PendingDecision` record *(new)*: `{ Id, Kind, Prompt, CreatedAt }`. (`RunId`
  joins this when the inbox needs to group by run — §13.)
- `InteractiveResolver` *(new)*: on `ResolveAsync`, `Register` then `await`.
- **Fulfillment:**
  - **Built now:** a **scripted fulfiller** — a test/dev component that watches
    `List()` and calls `Resolve(id, answer)` from a script, so flows run
    end-to-end without UI.
  - **Specified, deferred to UI:** `POST /runs/{runId}/decisions/{id}` → calls
    `Resolve`. The registry contract is the UI's exact integration point; nothing
    here is throwaway.

---

## 8. LLM resolver — DEFERRED

> Not built. Auto already covers "proceed autonomously without a human"; an LLM
> resolver only earns its place when we want a *separate* brain (a different/cheaper
> model, or context-isolated decider) making the call rather than nudging the stuck
> agent. `Resolution.Llm` stays a reserved enum value the factory rejects until
> then. The sketch below is what to build when that need is real.

`LlmResolver(IAgentFactory factory)` — a single `IDecisionResolver`. On
`ResolveAsync` it mints an **easy-to-configure** sub-agent via `IAgentFactory`
(its own small config: model + a focused "pick the best option, justify briefly"
system prompt), runs it **`Resolution.Auto`, cap 0** so it cannot itself park a
`NeedsInput` (no recursion), and returns the agent's answer. If a separate
justification is wanted, that's when `DecisionDto` regains a `Rationale` field
(it carries only the answer today — §6).

**No `IRationalizer` seam.** The rationalizing strategy is *already* swappable
through `IAgentFactory` + the sub-agent's config (model, prompt) — a second
interface on top of the first would be an abstraction on its first use. If a
genuinely non-agent rationalizer ever appears, extract the interface then; it's a
small refactor with tests already green.

---

## 9. Deny resolver

`DenyResolver : IDecisionResolver` *(new)* — refuses every decision:
- Permission Ask (`Choice`) → returns the **deny** option value.
- Open question → `null` (terminal `NeedsInput`), i.e. "I won't decide; stop."

Thin, no dependencies. For agents that must never proceed on a gated/ambiguous action.

---

## 10. Testing strategy

- **Resolvers** — unit tests per resolver against a `FakeProvider`/canned
  question: `DenyResolver` refuses (deny option for a `Permission`, `null` for a
  `Question`); `LlmResolver` mints + returns the sub-agent's answer (fake
  `IAgentFactory`).
- **Await** — `InteractiveResolver` blocks; a scripted fulfiller `Resolve`s →
  the run resumes to completion; run-cancel → `null` → terminal `NeedsInput`.
- **Audit** — after a run, the `FlowSnapshot` carries the expected
  `DecisionRecord`s (auto assumption, human answer, llm rationale, deny) with the
  right `Resolution` source; persisted history round-trips them.
- **Cap** — `Auto`/`Llm` loops fault at the cap; `Deny`/`Human` never cap.
- Deterministic only; no live CLI needed (existing live matrix already covers the
  real providers).

---

## 11. Build order (each an independently-buildable, tested commit)

1. **DP2 seam + DP1 axis.** Add `DecisionKind` to `ResolveAsync`; thread `Kind`
   through the Agent loop. Add `Resolution`, retire `Interactivity` (map values),
   update `AgentFactory` dispatch + cap predicate. Green build/tests.
2. **Deny resolver.** Smallest new resolver; wire `Resolution.Deny`.
3. **Audit ledger.** `DecisionRecord` (source = `Resolution`) + `FlowContext`
   ledger → `FlowSnapshot` → history; record auto + resolver-path decisions;
   retire the in-memory queue.
4. **Pending-decision backend + InteractiveResolver.** Registry, await, `ct`
   cancel linkage; scripted fulfiller; wire `Resolution.Human`. Specify (not
   build) the endpoint.
5. ~~**LLM resolver.**~~ **Deferred** (§8) — `Resolution.Llm` left reserved.

---

## 12. Done when

- The built resolution modes (`Auto`/`Deny`/`Human`) selectable per agent and
  exercised end-to-end through `Flow.Run` in tests; `Llm` reserved (deferred, §8).
- `InteractiveResolver` genuinely blocks until a scripted `Resolve` (or run-cancel). ✅
- A completed run's snapshot carries the `DecisionDto`s (auto proceed-instructions,
  human answers, deny) with the right source; the provisional `Assumptions` queue
  is gone. ✅
- Warning-free build, green tests. No UI, no fulfill-endpoint (both specified, deferred). ✅

---

## 13. Non-goals / deferred (the UI edge)

- **LLM resolver** (§8) — deferred until we want a *separate* brain deciding;
  `Resolution.Llm` is reserved meanwhile.
- **Provider-internal permission-verdict audit** (was DP3) — surfacing
  `AutoPolicy` allow/deny verdicts on `DriveResult` and draining them into the
  ledger. A fast-follow once the ledger exists and we can see whether we miss it;
  the resolver-path audit ships first.
- The **fulfill endpoint** (`POST …/decisions/{id}`) and the **attention-inbox UI**
  — built with the UI work; the registry contract is their integration point.
  `RunId` joins `PendingDecision` / the resolve seam here, when the inbox groups by run.
- **Timeout/escalation** on a waiting decision — decided out; run-cancel is the
  only unblock besides an answer.
- **`Choice` auto-match + picker UX** — stays with the UI/interaction-modes work.
