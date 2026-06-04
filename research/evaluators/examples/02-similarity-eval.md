# Example 2 — Similarity eval ("is the agent's result like what I expect?")

> **Purpose**: a worked, illustrative example of an **offline evaluation** —
> run an agent over a *dataset*, grade each output against a *reference*,
> *aggregate* into a score. No reaction; the score drives prompt-tuning.
> Pseudocode + made-up context.
>
> **Reads with**: [`../01-evaluation-and-grading-research.md`](../01-evaluation-and-grading-research.md)
> §4 (reference-based) and §5 (offline vs. online).
>
> **Status**: pedagogical snippet. Not a design.

---

## Where this sits

| Axis | This example |
|---|---|
| Anchor | a **reference** (known expected outputs) |
| Subject under test | **the agent** (its prompt/config) |
| Activity | **offline evaluation** |
| Output | **aggregate** score over a dataset |
| Reaction | **none** — you tweak the prompt and rerun |

This is the "evaluate the agent / validate it's doing a good job" case — the thing
you reach for when **tuning prompts**.

---

## Shared primitive

Same `Verdict` / `llmJudge` as [Example 1](01-rule-guardrail.md#the-shared-primitive-used-by-all-three-examples).
The grader atom does not change between guardrail and eval — only the harness
does.

---

## The dataset of `(input → expected)` pairs (made up)

The dataset **is** the eval. Each row is an input the agent will see and the
output we'd accept as correct.

```ts
const fixtures = [
  { diff: "added retry with backoff to HttpClient calls",
    expected: "Add exponential backoff retry to HTTP client" },
  { diff: "renamed UserSvc to AccountService across 12 files",
    expected: "Rename UserSvc to AccountService" },
  { diff: "fixed null deref in cart total when coupon is empty",
    expected: "Fix null reference in cart total for empty coupon" },
]
```

(In a real eval you'd have dozens — and, per the research, you want a balanced
spread, including cases the agent tends to get *wrong*.)

---

## The agent under test — a black box, input → output

This is the thing being graded. We don't care how it works; we feed it the input
and capture the output.

```ts
const prTitleAgent = (diff: string) =>
  agent.run(`Write a one-line PR title for this change:\n${diff}`)
```

---

## The reference-based judge — equivalence, not exact match

We can't `===` the strings — "Add exponential backoff retry to HTTP client" vs.
"Add HTTP client retry w/ exp. backoff" should both pass. So the judge decides
*semantic equivalence* against the reference. (This is the `factuality.yaml`
family from research §6.2 — note `{expected}` is present, which makes it
**reference-based**.)

```ts
const EQUIVALENT = `
You are comparing a produced answer to an expected answer.
[Expected]: {expected}
[Produced]: {produced}
Ignore wording, casing, and style. Do they convey the SAME change?
Reason step by step, then print only Y or N on its own line. Repeat the letter.
Reasoning:`

const isEquivalent = (produced: string, expected: string) =>
  llmJudge(EQUIVALENT.replace("{expected}", expected).replace("{produced}", produced))
```

---

## The harness — loop the dataset, run the agent, grade, aggregate

```ts
async function runEval() {
  const results = []
  for (const f of fixtures) {
    const produced = await prTitleAgent(f.diff)          // run the black box on a known input
    const v = await isEquivalent(produced, f.expected)   // grade against the reference
    results.push({ ...f, produced, pass: v.pass, why: v.why })
  }
  const score = results.filter(r => r.pass).length / results.length
  report(`agent score: ${(score * 100).toFixed(0)}%`)    // a NUMBER, for tuning
  return { score, results }                              // nobody is "fixed" — you tweak the PROMPT and rerun
}
```

The loop is the entire activity: **tune the agent prompt → rerun → compare
score.** That is what "evaluating the agent" *means*.

---

## What this example proves

- **The subject is the agent, not one output.** You cannot judge an agent from a
  single run, so the **dataset + aggregate** is the whole point. Contrast Example
  1, which judged a single artifact.
- **Reference-based.** `{expected}` is present; the judge compares to a gold
  answer. Contrast Example 1 (a rule, no reference) and Example 3 (no reference at
  all).
- **No reaction.** The output is a `score` you read. Nothing is blocked or
  refactored — you change the *prompt under test* and rerun. The grader stays
  pure, exactly as in Example 1.
- **Same atom, different machine.** `llmJudge` is unchanged; only the harness
  around it differs (`dataset → aggregate` instead of `one artifact → act`).

---

## The dependency this exposes in our stack

To run this *over our orchestrator's real agents*, the harness needs the recorded
`(input, output)` of past runs — or a way to replay against fixtures. Today
`OperationRecord` keeps only a summary string. This is the **trace seam** (ADR
0003 §4) that offline eval forces us to finally build. See research §9.
</content>
