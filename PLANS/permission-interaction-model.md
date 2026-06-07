# Permission + Interaction model — kill Sandbox, one resolve seam

> **Status.** DRAFT (planning, 2026-06-07). A simplification pass over the landed
> permission work ([ADR 0007](../design/adr/0007-permission-policy-pretooluse.md),
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

## 3. Scope

**In:**
- Remove the Sandbox layer (config field, per-agent knob, read-only Reviewer).
- Rename the resolver `IQuestionResolver` → `IDecisionResolver` (done).
- Add the **`Interactivity {Interactive, Autonomous}`** flag + the **Autonomous path**
  (directive selection + auto-resolver + recording) — buildable pre-UI.
- Keep `PermissionPolicy {Bypass, Auto, Ask}`, `ClaudeHooks`, `AutoPolicy`,
  `IDecisionResolver`, the pump, the Codex Bypass-guard.
- Realign docs (ADR 0007, permission-policy-plan, memory) to the model above.

**Out / deferred (unchanged):**
- **Interactive mode's human side** — the human resolver + end-of-turn resume loop —
  the UI / terminal-driven work. Pre-UI an Interactive agent's question goes terminal
  (`NeedsInput`, no resume). Autonomous needs none of this.
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
3. Merge — pending owner go.

## 8. Non-goals

No human resolver, no end-of-turn resume loop, no capability/VM work, no Codex
approval — all deferred. This pass is the sandbox subtraction + the rename + the
**Autonomous** half of interactivity (which needs no human). The Interactive half
waits for the UI.
