# Example 3 — Black-box quality eval (no rule, no reference)

> **Purpose**: the hardest case — a black-box operation whose end result we must
> grade with **no rule** and **no reference**. The rubric carries everything, so
> this is where evidence-anchoring and judge-validation become mandatory.
> Pseudocode + made-up context chosen to *expose the trap*.
>
> **Reads with**: [`../01-evaluation-and-grading-research.md`](../01-evaluation-and-grading-research.md)
> §2 (judge bias), §4 (reference-free), §8 (anchoring spectrum).
>
> **Status**: pedagogical snippet. Not a design.

---

## Where this sits

| Axis | This example |
|---|---|
| Anchor | **nothing but the rubric** |
| Subject under test | one output (here), but the rubric also gates the agent |
| Activity | online or offline (the rubric is the same) |
| Reference | **none** — reference-free |
| The risk | the judge's own **bias** is the main failure mode |

This is the most demanding end of the [anchoring spectrum](../01-evaluation-and-grading-research.md#8-the-anchoring-spectrum-the-unifying-idea).

---

## The setup — a black box and a deceptively bad output

A merged PR goes into a black-box operation that writes a user-facing release
note. We only get the end result — no rule to check, no gold answer to diff
against.

```ts
const source = {
  title: "Perf + reliability pass on checkout",
  diff: `
+ cache.ttl = 300            // was 60
+ httpClient.retry(maxAttempts: 3, backoff: exponential)
+ if (coupon is null) return cartTotal;   // was: throw NullRef`,
}

const note = await releaseNoteAgent(source)   // <-- the black box. opaque. we only see its output:

// note === "We've made some exciting improvements to boost performance and
//           reliability! Your experience is now smoother than ever.
//           Enjoy faster load times and fewer hiccups. 🚀"
```

That note is **bad**: vague, and not one claim traces to the diff. But it's fluent
and upbeat — so a naive judge, riding its own positivity/verbosity bias, will
**pass it**. The example exists to defeat that.

---

## Insight #1 — reference-free is NOT context-free

The single most important move: you have no *gold answer*, but you **still pass the
source** so the judge can check grounding. Drop the source and *"fluent
hallucinations pass undetected"* (research §4). The judge sees `(source, output)`,
just not `(expected)`.

---

## Insight #2 — decompose the rubric into single-criterion binaries

A "is this good? 1–10" judge is where bias runs wild. Split into independent yes/no
criteria (the same fan-out as Ragas, research §6.3):

```ts
// ---- criterion 1: GROUNDED. every claim must trace to the diff. evidence-anchored. ----
const GROUNDED = `
You are checking whether a release note is grounded in the actual code change.
[Source diff]:
{diff}
[Release note]:
{note}
For EACH claim the note makes, quote the diff line that supports it.
If any claim has no supporting line, the note FAILS.
Reason claim-by-claim, then print only Y or N on its own line. Repeat the letter.
Reasoning:`

// ---- criterion 2: CLEAR for a non-technical user. one thing only. ----
const CLEAR = `
[Release note]: {note}
Rule: a non-technical user can understand it — no jargon, no internal terms.
Reason step by step, then print Y or N on its own line. Repeat it.
Reasoning:`

// ---- criterion 3: NO FLUFF. concrete, not empty marketing filler. ----
const NO_FLUFF = `
[Release note]: {note}
Rule: every sentence conveys a concrete change. Generic hype with no specific
information ("smoother than ever", "exciting improvements") FAILS.
Reason sentence by sentence, then print Y or N on its own line. Repeat it.
Reasoning:`

const fill = (t, src, note) => t.replace("{diff}", src.diff ?? "").replace("{note}", note)
```

The `GROUNDED` criterion is the load-bearing one: forcing *"quote the diff line"*
turns a vibes-judgment into a checkable one — **evidence-anchored scoring**
(RULERS, research §2).

---

## The harness — grade one artifact into a composite

```ts
async function gradeNote(source, note): Promise<{ grade: number, breakdown: Verdict[] }> {
  const breakdown = [
    await llmJudge(fill(GROUNDED, source, note)),
    await llmJudge(fill(CLEAR,    source, note)),
    await llmJudge(fill(NO_FLUFF, source, note)),
  ]
  const grade = breakdown.filter(v => v.pass).length / breakdown.length
  return { grade, breakdown }
}

// our example note scores 1/3:
//   GROUNDED  -> N  ("faster load times" has no supporting line; ttl/retry/coupon unmentioned)
//   CLEAR     -> Y  (a user understands it)
//   NO_FLUFF  -> N  ("smoother than ever", "exciting improvements" = hype)
//   grade = 0.33
```

The decomposition + evidence-anchoring is what catches what the judge's own bias
would wave through.

---

## Insight #3 — here, you MUST evaluate the judge itself

With a rule (Example 1) you trust the grader by reading it; with a reference
(Example 2) the gold answer anchors it. **Reference-free, the rubric is the only
anchor — so the rubric (and its judge) is now the thing most likely to be wrong.**
You validate it the same way you validate any grader: against a few human labels.

```ts
// ---- a tiny human-labeled calibration set: notes a human already graded pass/fail ----
const humanLabeled = [
  { source: srcA, note: "Checkout now retries failed requests automatically.", human: true  },
  { source: srcB, note: "We made things better! Enjoy. 🚀",                    human: false },
  { source: srcC, note: "Empty coupon codes no longer break the cart total.",  human: true  },
  // ...15-20 of these
]

async function validateJudge() {
  let agree = 0
  for (const ex of humanLabeled) {
    const { grade } = await gradeNote(ex.source, ex.note)
    const judgePass = grade >= 0.66          // your threshold choice
    if (judgePass === ex.human) agree++
  }
  const agreement = agree / humanLabeled.length
  report(`judge agrees with humans ${(agreement * 100).toFixed(0)}% of the time`)
  // < ~80%? the RUBRIC is broken, not the agent. fix the rubric, not the prompt-under-test.
}
```

The mental model to carry: **an offline eval tunes the agent; judge-validation
tunes the eval.** Reference-free, you cannot skip the second loop — nothing else
keeps the judge honest.

---

## Which biases bite here, and what fights them

| Bias (research §2) | How it shows up on our fluffy note | Mitigation in the snippets |
|---|---|---|
| **Verbosity / positivity** | judge passes the long upbeat note | `NO_FLUFF` + reason sentence-by-sentence |
| **Fluent hallucination** | "faster load times" sounds plausible, isn't in the diff | `GROUNDED` + **quote the diff line** (evidence-anchored) |
| **Vague mega-judge** | "7/10, feels good" | decomposed single-criterion **binaries** |
| **Self-preference** | judge likes LLM-style prose | the human calibration set catches systematic drift |

---

## The arc across all three examples

The grader atom never changed — `llmJudge → Verdict`. What changed is the
**anchor**:

- **[Example 1](01-rule-guardrail.md) (rule):** anchored by a rule → guardrail, reacts.
- **[Example 2](02-similarity-eval.md) (similarity):** anchored by a reference → offline eval, aggregates.
- **Example 3 (black box):** anchored by **nothing but the rubric** → so the rubric
  must be evidence-anchored *and* the judge validated against humans.

**Less anchor → more burden on the rubric → more need to evaluate the
evaluator.** That is the spectrum the eval layer has to span.
</content>
