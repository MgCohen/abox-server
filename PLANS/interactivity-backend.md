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

## 2. Locked decisions (from planning Q&A)

1. **Build all resolver variants** — Interactive (real await), LLM, Deny.
   (`AutoResolver` + `NonInteractiveResolver` already exist.)
2. **Real await mechanism.** A pending-decision registry + a resolver that
   **blocks until answered or run-cancelled** (no timeout). Only the HTTP
   fulfill-endpoint + UI are deferred; a **scripted/test fulfiller** drives it now.
3. **LLM resolver is decoupled** — it depends on an injected `IRationalizer`
   interface; a separate impl spawns an easy-to-configure agent to rationalize.
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
| `LlmResolver` *(new)* | Delegate to injected `IRationalizer` | yes (loops) |
| `DenyResolver` *(new)* | Always refuse — deny choice for a permission Ask; terminal for an open question | no (terminal) |
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
record DecisionRecord(
    DecisionKind Kind,
    string Prompt,
    string Answer,              // the answer / allow / deny text
    Resolution Source,         // Auto | Llm | Deny | Human — reuse the §4 enum
    string? Rationale,          // LLM rationale or auto assumption
    DateTimeOffset At);
```

`Source` reuses the `Resolution` enum from §4 — the source of a decision *is* the
resolver that made it, so a separate `DecisionSource` enum with the same four
members would only drift.

- **Collected on the run.** `FlowContext` gains a decision ledger
  (`RecordDecision` + a read-only list), surfaced on `FlowSnapshot` and persisted
  with the snapshot via `IFlowHistory` (no new store).
- **Recorded by the Agent resolve loop** at each resolution (it has the answer,
  the `Resolution` source, and the `FlowContext`). This covers every
  resolver-path decision: auto assumptions, human answers, llm rationales, deny.
- **Retire** `AutoResolver`'s in-memory `Assumptions` queue — its content becomes
  `DecisionRecord{Source=Auto}` on the run.

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

## 8. LLM resolver

`LlmResolver(IAgentFactory factory)` *(new)* — a single `IDecisionResolver`. On
`ResolveAsync` it mints an **easy-to-configure** sub-agent via `IAgentFactory`
(its own small config: model + a focused "pick the best option, justify briefly"
system prompt), runs it **`Resolution.Auto`, cap 0** so it cannot itself park a
`NeedsInput` (no recursion), and returns the agent's answer. The justification
lands in `DecisionRecord.Rationale`.

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
5. **LLM resolver.** `LlmResolver(IAgentFactory)`; wire `Resolution.Llm`.

---

## 12. Done when

- All four resolution modes (`Auto`/`Llm`/`Deny`/`Human`) selectable per agent and
  exercised end-to-end through `Flow.Run` in tests.
- `InteractiveResolver` genuinely blocks until a scripted `Resolve` (or run-cancel).
- A completed run's snapshot + persisted history carry the `DecisionRecord`s
  (assumptions, human answers, llm rationales, deny); the provisional
  `Assumptions` queue is gone.
- Warning-free build, green tests. No UI, no fulfill-endpoint (both specified, deferred).

---

## 13. Non-goals / deferred (the UI edge)

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
