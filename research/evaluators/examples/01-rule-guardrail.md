# Example 1 — Rule guardrail ("does this output break a coding rule?")

> **Purpose**: a worked, illustrative example of an **online guardrail** — judge
> *one* artifact against a rule, then *react*. Pseudocode + made-up context;
> deliberately not our project's types.
>
> **Reads with**: [`../01-evaluation-and-grading-research.md`](../01-evaluation-and-grading-research.md)
> §5 (offline vs. online) and §8 (anchoring spectrum).
>
> **Status**: pedagogical snippet. Not a design.

---

## Where this sits

| Axis | This example |
|---|---|
| Anchor | a **rule** (no reference, no dataset) |
| Subject under test | **one artifact** (a diff) |
| Activity | **online guardrail** |
| Output | per-instance pass/fail |
| Reaction | **yes** — re-prompt the agent to refactor |

This is the "evaluate a piece of work and act on it" case.

---

## The shared primitive (used by all three examples)

```ts
// A grader returns a verdict and nothing else. `why` is exhaust — for humans, never aggregated.
type Verdict = { pass: boolean, why: string }

// An LLM judge = fill a prompt, parse a trailing Y/N. (closedqa pattern: reason first, label last.)
async function llmJudge(filledPrompt: string): Promise<Verdict> {
  const out = await llm.complete(filledPrompt, { model: "small-fast", temperature: 0 })
  return { pass: out.trim().endsWith("Y"), why: out }
}
```

Everything below is just *what you wrap around `llmJudge` (or a deterministic
check)*.

---

## The artifact under test (made up)

A refactor agent produced this diff. We want a gate that decides whether it may
proceed.

```ts
const diff = `
+ public void SyncInventory() {
+     Console.WriteLine("syncing...");
+     var items = _repo.LoadAll();
+     foreach (var i in items) { Push(i); Log(i); Audit(i); Retry(i); }
+ }`
```

Two things are wrong with it, and they need **two different kinds of check** —
which is the whole point of this example (the "hybrid norm").

---

## (a) Deterministic grader — a rule that is a fixed function of the text

No model. Just count and threshold. Cheap, reproducible, trustworthy-by-reading.

```ts
function noConsoleInProd(diff: string): Verdict {
  const hits = matchCount(diff, /^\+.*Console\.WriteLine/gm)   // only added (+) lines
  return { pass: hits === 0, why: `${hits} Console.WriteLine in new code` }
}
```

Use deterministic whenever the rule *can* be a fixed function of the output —
format, schema, lint count, banned API, presence/absence. Never spend a model
call on something a regex settles.

---

## (b) LLM-judge grader — a fuzzy rule, one criterion, binary

"Single responsibility" can't be a regex. It needs judgment — but kept to **one
criterion**, **binary**, **reason-then-label** (the `closedqa` shape from the
research doc §6.1).

```ts
const SINGLE_RESPONSIBILITY = `
You are checking ONE coding rule against a code diff.
[BEGIN DATA]
[Rule]: A method should do one thing. Flag a method that mixes unrelated
        responsibilities (e.g. persistence + logging + retry + auditing inline).
[Diff]:
{diff}
[END DATA]
Does the new code satisfy the rule? Reason step by step first.
Then print only Y or N on its own line. Repeat the letter on a final line.
Reasoning:`

const singleResponsibility = (diff: string) =>
  llmJudge(SINGLE_RESPONSIBILITY.replace("{diff}", diff))
```

Note what the prompt does **not** contain: no reference answer, no whole codebase
— just the rule and the diff. That is *criterion-scoped input* (research §4).

---

## The harness — check one artifact, then react

```ts
async function ruleGate(diff: string, agent): Promise<boolean> {
  const checks = [ noConsoleInProd(diff), await singleResponsibility(diff) ]
  const failures = checks.filter(c => !c.pass)

  if (failures.length > 0) {
    // The REACTION lives in the harness/flow — NOT in the grader.
    const feedback = failures.map(f => f.why).join("\n")
    await agent.run(`Your change broke these rules — fix them:\n${feedback}\n\nDiff:\n${diff}`)
  }
  return failures.length === 0   // the gate: proceed, or loop back
}
```

---

## What this example proves

- **Hybrid norm in one function.** A deterministic check and an LLM check sit side
  by side behind the same `Verdict`. Pick deterministic when you can, LLM when you
  must.
- **The grader stays pure.** `noConsoleInProd` and `singleResponsibility` return
  verdicts and nothing else. The **react** (re-prompt to refactor) is the
  *harness's* job. That seam is exactly what lets `singleResponsibility` be reused
  unchanged inside an offline eval (Example 2/3) where there is no reaction.
- **Grade vs. react are different verbs.** The grade answers "is it OK?"; the
  reaction ("fix it") is a separate decision owned by the flow.

---

## If you wanted the *reviewer* instead of the *guardrail*

Same checks, different *retention*. Instead of collapsing to `pass/fail` and
acting, keep each failure as a finding with its location — and you have a
**reviewer** (research §3, "review ⊇ grade"). The Qodo prompt (research §6.4)
does exactly this and *also* emits a single `score`, showing both outputs from one
engine.
</content>
