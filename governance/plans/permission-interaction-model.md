# Permission + Interaction model — kill Sandbox, one resolve seam

> **Status.** DRAFT (planning, 2026-06-07). A simplification pass over the landed
> permission work ([ADR 0007](../decisions/0007-permission-policy-pretooluse.md),
> [permission-policy-plan.md](permission-policy-plan.md)). No new capability; this
> *removes* a layer and fixes the conceptual relationships. Pre-UI.

## 1. Why

Three concerns had bled into each other:

- **Sandbox** (capability — what the agent can touch),
- **Permission** (approval — who authorizes an action),
- **Interaction** (input — the agent asking a human for information).

The conflicts: `PermissionPolicy` meant *approval* for Codex but *approval +
capability* for Claude (whose `permission-mode` fuses both); `CodexConfig.Sandbox`
looked like a per-agent policy but is bypassed on Windows and isn't really ours; and
a permission-approval was routed through the *same* seam as agent questions.

We never used the read-only Reviewer, and the real capability wall is the VM later.
So: **delete Sandbox as a layer now.** That collapses three concerns to two and
removes the cross-meaning.

## 2. The model (after)

Two concerns, one shared resolve seam:

| Concern | Question | Decider |
|---------|----------|---------|
| **Permission** (per-agent gate) | An action is allowed — who authorizes it? | `Bypass` → nobody · `Auto` → `AutoPolicy` · `Ask` → the resolve seam |
| **Interaction** (agent-initiated) | Agent needs information to continue | the resolve seam — **human** (Interactive) or **auto-answer + record** (Autonomous), per the agent's `Interactivity` flag |

- **Resolve seam** (`IDecisionResolver`) is the single "agent is blocked, needs an
  outside decision" boundary, shared by `Ask`-permission **and** agent questions.
  Pre-UI it is a stub (deny / null); the UI later swaps in a real human.
- **`AutoPolicy`** is the *automatic* (no-human) permission decider — the denylist
  guardrail. It is **separate from** the resolve seam (which is the *human* decider).
- **Sandbox is gone.** Capability is deferred to the VM/host; we don't model it.

Relationship in one line: the **gate** (permission) and the **intercom**
(interaction) are different events that share one outside decider (resolve); the
**wall** (sandbox) is no longer our concern — it's the host's, later.

Permission-`Ask` stays modeled as an `AgentQuestion.Choice` through the one resolver
(intentionally — this *is* the single resolve seam; un-merging would add a piece).

### Interactivity (per-agent)

A second per-agent flag, **`Interactivity { Interactive, Autonomous }`**, controls how
the agent's *questions* are handled — orthogonal to Permission (which gates *tools*). It
sets two things together:

1. **The system-prompt directive.**
   - *Autonomous* → "make a reasonable assumption, state it, and continue; only ask as a
     last resort" (today's `AgentDirective.Unattended`).
   - *Interactive* → "when you genuinely need input, ask via `<<NEEDS_INPUT>>` and wait."
2. **The decider behind the resolve seam** for that agent.
   - *Autonomous* → an **auto-resolver**: self-answers with a sensible default and
     **records** the assumption to the run record, so the run never blocks.
   - *Interactive* → the **human** resolver (pre-UI the stub leaves the question terminal;
     post-UI a human answers and the run resumes).

So an Autonomous bot "just keeps going": the directive keeps it from asking, and the
auto-resolver is the net that self-answers + records if it asks anyway. An Interactive
bot routes questions to a human through the same seam.

**Buildable pre-UI:** Autonomous is fully buildable now (directive + auto-resolver +
recording — no human needed) and is the useful-now default. Interactive's human side
lands with the UI; pre-UI an Interactive agent's question just goes terminal (today's
behavior).

**Wiring:** `AgentFactory` picks the resolver from `config.Interactivity` (Autonomous →
auto-resolver, Interactive → the human/stub resolver) and threads it into the provider
(already the seam). Directive selection moves into `AgentDirective.Compose` by the flag.

**The `Ask` × `Autonomous` overlap — resolved (keep orthogonal, document).** Both
`Ask`-permission and questions use the agent's resolver, so for an *Autonomous* agent a
tool-`Ask` is answered by the auto-resolver (≈ `Auto`). We keep the two axes orthogonal —
collapsing `Permission` into `Interactivity` would lose the useful `Interactive` + `Auto`
combo (human answers questions, the guardrail decides tools) — and simply document that
**`Ask` presumes `Interactive`**: an `Autonomous` + `Ask` agent behaves like `Auto`. No
coercion code.

### The resume loop (the Agent owns it)

There are **two question channels**, and they already live in different places:

- **Mid-turn permission gate** (`PreToolUse`) — resolved *inside one* `provider.DriveAsync`
  call; the resolver is already injected into `ClaudeProvider`. No loop leaks to the flow.
- **End-of-turn question** (`<<NEEDS_INPUT>>`) — `Agent.Invoke` parses it into `NeedsInput`.
  Answering it means **running the agent again** with the answer as the next prompt (resume).

The end-of-turn resume loop is what would otherwise be copy-pasted into every flow. **The
`Agent` owns it**, because the Agent already carries the session (`_sessionId`) across turns
and already knows how to "run again," and the loop is provider-agnostic (every provider emits
the same envelope) so it belongs *above* the provider. `AgentFactory` injects the **same
resolver instance** into the Agent (end-of-turn questions) that it already injects into the
provider (mid-turn permissions) — one seam, both surfaces.

`Agent.Invoke` becomes a resolve→resume loop:

```
// resolver + int? resolveCap injected into the Agent by AgentFactory
count = 0
run turn
while outcome is NeedsInput:
    if resolveCap is {} cap && count >= cap:
        return Faulted("auto-resolution exhausted after N rounds")   // only reachable when bounded ⇒ auto
    answer = await resolver.ResolveAsync(needs.Question, ct)
    if answer is null: break        // human said "done" / declined ⇒ terminal NeedsInput (escalate)
    count++; outcome = run next turn with answer
return outcome
```

**The cap is auto-only, and it's the factory's call — not the resolver's, not the Agent's.**
A human resolver self-terminates (it returns null when the person is done), so it needs no cap
and must not be forced to stop. `AutoResolver` never self-terminates (it self-answers forever),
so it needs one. The cap *value* is a per-agent config knob — `AgentConfig.ResolveCap` (default
8) — and `AgentFactory` passes `config.ResolveCap` for Autonomous, `null` for Interactive, into
the `Agent` ctor. The *counter* lives in the Agent's per-run loop (a local var, so it resets each
`Invoke`; this is also why it can't live on the singleton `AutoResolver`). The Agent honors a
nullable cap generically — it never learns the word "Autonomous" and never sniffs the resolver,
and nothing lands on `IDecisionResolver`. The two break branches give exactly the three behaviors:

- **Autonomous** → bounded; a runaway question-loop `Faulted`s loudly (a stuck loop is a malfunction).
- **Interactive/human** → `ResolveCap` null, cap branch unreachable; the human runs as long as
  they like and stops by returning null → clean terminal `NeedsInput`, never a fault.
- The Agent never learns its own `Interactivity` — it just "resolves until null, or until the
  resolver's own cap." No mode plumbed in, no type-sniffing.

**Buildable pre-UI, no regression.** Autonomous now actually resolves + resumes in production
(its first real trigger — the win). Interactive pre-UI still wires the stub resolver, which
returns null on the first question → the loop breaks immediately → terminal `NeedsInput`
(today's exact behavior). The human side only changes when the UI swaps a real resolver in.

## 3. Scope

**In:**
- Remove the Sandbox layer (config field, per-agent knob, read-only Reviewer).
- Rename the resolver `IQuestionResolver` → `IDecisionResolver` (done).
- Add the **`Interactivity {Interactive, Autonomous}`** flag + the **Autonomous path**
  (directive selection + auto-resolver + recording) — buildable pre-UI.
- Keep `PermissionPolicy {Bypass, Auto, Ask}`, `ClaudeHooks`, `AutoPolicy`,
  `IDecisionResolver`, the pump, the Codex Bypass-guard.
- Realign docs (ADR 0007, permission-policy-plan, memory) to the model above.

- **The Agent-owned resume loop** (`Agent.Invoke` resolves end-of-turn `NeedsInput`
  through its injected resolver, bounded by `ResolveCap` only when auto). Gives the
  Autonomous path its production trigger; Interactive stays terminal pre-UI (stub returns
  null → loop breaks → `NeedsInput`).

**Out / deferred (unchanged):**
- **Interactive mode's human side** — the real human resolver behind the seam (a blocking
  await on a person) — the UI / terminal-driven work. The *loop* exists; pre-UI it just
  breaks immediately on the stub's null. Autonomous needs none of this.
- **Capability / sandbox** — re-introduced (if ever) as a VM/host boundary, not a
  per-agent enum. The plan's old open Q1 (capability-vs-approval) is hereby closed:
  we model approval only.
- **Codex `Ask`** — still Bypass-only on the approval axis (the guard stays).

## 4. Changes (mostly deletion)

1. **`CodexConfig`** — drop the `Sandbox` field.
2. **`CodexProtocol`** — drop the `sandbox` parameter from `BuildArgs`; bake one
   internal default flag so `codex exec` has a valid arg
   (`danger-full-access` on Windows, `workspace-write` elsewhere). `EffectiveSandbox`
   becomes the no-arg internal default.
3. **`Agents.Reviewer`** — drop `Sandbox: "read-only"`.
4. **Tests** — update `ProviderPolicyTests` (the Codex new-turn/resume sandbox
   assertions now check the baked default; the Bypass-guard test stays).
5. **Docs/memory** — ADR 0007: strike the read-only Reviewer + capability-axis (Q1)
   notes, state the two-concern model. `permission-policy-plan.md`: point to this doc
   for the model. Retire the `codex-windows-sandbox-readonly-caveat` memory (moot).

## 5. Tests

- Existing suite stays green; `ProviderPolicyTests` adjusted for the baked default.
- No new live cells needed — behavior is unchanged except the Reviewer is no longer
  nominally read-only (it already ran full-access on Windows, so no real change).

## 6. Resolver naming — DONE

`IQuestionResolver` → **`IDecisionResolver`** (it openly resolves both permissions and
questions); `NonInteractiveResolver` stays as the stub impl.

## 7. Build order

0. Resolver rename `IQuestionResolver` → `IDecisionResolver` — **done**.
1. **Kill Sandbox** (changes 1–4) + realign docs/memory (change 5) — **done**
   (commit `a68b561`, the "simplification" commit, folded in the rename).
2. **Interactivity (Autonomous path)** — **done**: `Interactivity` flag on `AgentConfig`
   (default `Autonomous`, behavior-neutral); `AgentDirective.ComposeSystemPrompt` selects
   the directive by it (shared envelope-format, two preambles); `AutoResolver` self-answers
   + records; `AgentFactory` picks `AutoResolver` (Autonomous) vs the human/stub resolver
   (Interactive). Unit tests in `InteractivityTests`.
3. **Resume loop (Agent owns it)** — **done**. The resolver + `int? resolveCap` are injected
   into `Agent` by `AgentFactory` (same resolver instance as the provider's; cap is
   `config.ResolveCap` for Autonomous, `null` for Interactive). `Agent.Invoke` is the
   resolve→resume loop (per-run counter, `Faulted` on cap, terminal `NeedsInput` on null);
   the per-agent cap lives on `AgentConfig` (default 8) — nothing on `IDecisionResolver`, no
   transient. The test-only `ResolvingFlow` is gone, its coverage lifted into Agent-level unit
   tests (`AgentOutcomeTests`), plus the three live matrix cells (`InteractivitySmokeTests`)
   and the two `Missing_secret` ping cells refreshed to expect self-resolution under autonomy.
4. Merge — pending owner go.

## 8. Non-goals

No *real human* resolver (the seam's Interactive impl is still the null stub), no
capability/VM work, no Codex approval — all deferred. This pass is the sandbox
subtraction + the rename + the **Autonomous** half of interactivity **plus the
Agent-owned resume loop** that gives it a production trigger. The real human resolver
behind the seam waits for the UI.
