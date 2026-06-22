# Evaluation & Grading — Research

> **Purpose**: settle the conceptual model for *evaluators* before we design an
> eval layer for the orchestrator. What an evaluator **is**, how grading differs
> from reviewing, what the input should be, and the offline-eval vs.
> online-guardrail split — grounded in respected sources and shipped open-source
> prompts.
>
> **Audience**: anyone designing the ABox eval/guardrail layer (the
> natural layer after the flow lands). Pairs with the worked snippets under
> [`examples/`](examples) and the actor/operation model in
> [`design/adr/0003-actors-operations-run-contract.md`](../../../decisions/0003-actors-operations-run-contract.md).
>
> **Status**: research complete 2026-06-03. Conceptual only — **no implementation
> decided**. This document captures findings; it is not a plan.

---

## TL;DR — the whole arc in nine claims

1. **An evaluator is three steps: `input → check → grade`.** The check is the
   only interesting part; the output is a grade and (optionally) a throwaway
   rationale.
2. **Prefer binary/discrete grades over numeric scores.** Let the model
   *classify*; let your *code* assign the number.
3. **The grade is the contract; the rationale is exhaust** — useful for
   debugging, never aggregated as data.
4. **One grader = one criterion.** Compose many small binary graders; never build
   one mega-judge.
5. **The check is either deterministic or a simple LLM-as-judge** — and the field
   default is *both* (the "hybrid norm").
6. **Grader vs. reviewer is a real distinction**: measurement vs. actionable
   feedback. **Review ⊇ grade** — a grade is a projection of a review onto a
   scalar; you can derive a grade from a review but never the reverse.
7. **The grader's input is a triple** — `(task, output-under-test, optional
   reference)` plus *criterion-scoped* context. For open-ended work you carry a
   **rubric instead of a reference** (reference-free).
8. **Two activities the literature keeps apart**: **offline evaluation** (subject
   = the agent; dataset + references; aggregate; no consequence; for tuning) vs.
   **online guardrails** (subject = one output; runtime; reference-free;
   per-instance; *acts* on the artifact). The discriminator is **subject under
   test + consequence**.
9. **An LLM judge is itself something you must evaluate.** The less you anchor it
   (rule → reference → rubric), the more the rubric carries, and the more you
   must validate the judge against human labels.

---

## 1. What an evaluator is

The canonical shape, confirmed across every framework surveyed:

```
input  ──▶  check (scorer / grader)  ──▶  grade
```

In the field the middle thing is a **scorer** or **grader**. An **eval** is then
that grader run over a *dataset* of examples and aggregated; the grader is the
atom, the eval is the atom at scale.

### Grade shape — prefer binary, distrust fine-grained numbers

A grade is `true/false`, pass/fail, or a small discrete bucket. The strong field
lean: **binary or low-cardinality discrete (pass/fail, 1–4) beats 0–100.** LLM
judges are noisy at fine resolution — a "73 vs 78" is invented precision — but a
pass/fail judge is reliable: an LLM judge agrees with human reviewers **~85% of
the time, higher than two humans agree with each other**, which is the bet behind
LLM-as-a-judge ([Confident AI](https://www.confident-ai.com/blog/why-llm-as-a-judge-is-the-best-llm-evaluation-method)).
This pairs with the "simple yes/no prompt" instinct: lots of small binary
graders, not one grader emitting a rich number.

### Output — grade is the contract, rationale is exhaust

The **grade** is the only thing you act on or aggregate. A one-line **rationale**
("failed because it added a comment") is the thing you read when a grade
surprises you — keep it, but never let anything *depend* on it or aggregate it.
The validated prompts enforce this structurally: reason first, then print the
label (see `cot_classify`, §6.1).

### One grader = one criterion

The biggest conceptual trap is a mega-grader judging five things at once. Keep
each grader single-purpose ("is it concise?", "did it touch only intended
files?"). You compose *many small graders*; you don't build *one smart one*. This
is why "the check is simple" is the design, not a limitation. Rubric-based
methods in the wild decompose for exactly this reason (§6.3, Ragas).

---

## 2. Deterministic vs. LLM-as-judge — the "hybrid norm"

The check engine is one of two kinds, and best practice uses **both**, not one:

| | **Deterministic** | **LLM-as-judge** |
|---|---|---|
| What | a fixed function of the output | a model scores against a written rubric |
| Examples | lint-error count vs. threshold, exact match, schema-valid, keyword present, tests pass | "is it coherent?", "does it follow the existing pattern?", "does this *feel* like X?" |
| Trust by | reading the code | **validating against human labels** |
| Cost | ~free, reproducible | model call, must mitigate bias |

> *"Deterministic scorers handle the clear-cut stuff: format validation, length
> checks, schema compliance, keyword presence, while LLM judges handle the
> fuzzier criteria like instruction-following, conciseness, and factual
> grounding. Rubrics are not replacing deterministic unit tests; they are joining
> them in a 'Hybrid Norm.'"* ([Braintrust](https://www.braintrust.dev/articles/what-is-llm-as-a-judge),
> [Future AGI](https://futureagi.com/blog/llm-evaluation-frameworks-metrics-best-practices/))

### An LLM judge is not free truth

Four biases appear in **every untreated judge pipeline** — **position,
verbosity, self-preference, authority** — and mitigation is a *design
requirement*, not an optimization ([Masood, 2026](https://medium.com/@adnanmasood/rubric-based-evals-llm-as-a-judge-methodologies-and-empirical-validation-in-domain-context-71936b989e80),
[RULERS, arXiv 2601.08654](https://arxiv.org/pdf/2601.08654)). A judge's own
prompt is itself a prompt to engineer (role + rubric-as-context + scoring action
+ strict output format — "RCAF"). **Mental note for the whole layer: a judge is a
model you also have to evaluate.**

---

## 3. Grader vs. reviewer — measurement vs. actionable feedback

A genuine and named distinction. The axis is **what the output is for and who
consumes it** — *not* the number of rules or whether you use sub-agents.

| | **Grader / evaluator** | **Reviewer / critic** |
|---|---|---|
| Output | a **measurement** (scalar/label) | a set of **findings** (issue + location + fix) |
| Direction | **reductive** — throws detail away for a comparable number | **expansive** — produces detail |
| Consumer | *you / the optimization loop* — "how good is this?" | *the agent or human who acts* — "what do I change?" |
| Must be | aggregatable, comparable across runs | actionable |

The literature draws this line explicitly: critique is *"identify flaws **and
provide constructive, actionable feedback**,"* and its quality is judged by
**downstream impact** — did the critique make the next revision better — not by a
score in isolation ([RealCritic, arXiv 2501.14492](https://arxiv.org/pdf/2501.14492),
[CritiqueLLM, arXiv 2311.18702](https://arxiv.org/pdf/2311.18702)).

### The key relationship: review ⊇ grade

A reviewer is a **superset** of a grader. Review produces findings; a grade is a
**projection** of those findings onto a scalar:

- You can **always derive a grade from a review** — count findings,
  severity-weight, threshold. ("5 rules → 5 yes/nos → score 1–5" *is* this
  projection.)
- You can **never derive a review from a grade** — the scalar already discarded
  the detail.

So "10 code standards, judged one at a time" is a **reviewer** whose output you
*can* project into a grade if you also want to track it. **Same underlying
checks; the difference is what you retain** — keep per-standard findings with
locations → review; keep only the count → grade.

### Decomposition is orthogonal

"Split each rule into its own single-criterion binary judge" is **not** what makes
something a grader or a reviewer — it's a **reliability technique** that serves
*both*. The same decomposed fan-out can emit a grade (sum the binaries) **or** a
review (collect each judge's finding). Decomposition is *how you run the checks
reliably*; grade-vs-review is *what you do with the results*.

### Reviewers also need validation

Human and LLM reviewers **diverge on what they flag** — humans weight
novelty/clarity, LLMs weight rigor/technical detail ([LLM peer-review study,
arXiv 2509.09912](https://arxiv.org/pdf/2509.09912)). A reviewer, like a judge,
is something you eventually have to validate.

**Build implication:** don't build "graders" and "reviewers" as two species.
Build one decomposed, single-criterion check engine. A **grader** = that engine
with a *projection-to-scalar* on the end; a **reviewer** = the same engine that
*retains the findings*. Pick by consumer.

---

## 4. The input to a grader

There is an established schema. An eval example is, at most:

```
(input, output-under-test, optional reference) + rubric + just-enough context
```

Mapped to "I asked an agent for a quick refactor; grade the result":

- **input** = the **prompt** you sent ("refactor X"). Needed to judge *"did it do
  what was asked?"*
- **output-under-test** = the **diff / changed files**. **Not the whole
  codebase** — the single most common mistake: it's noise, cost, and context-limit
  pressure, and dilutes the judge's attention from the change.
- **reference** = the expected/"what it's supposed to look like" — **usually
  absent** for open-ended code work (see below).
- **context** = *just enough* surrounding code for the specific criterion
  (criterion-dependent: "only touched intended files?" needs the file list; "is
  it idiomatic?" needs the neighbours; "does it compile?" needs the build).

### Reference-based vs. reference-free — the piece that resolves the confusion

"...just the prompt, and *what it's supposed to look like*?" — that "what it's
supposed to look like" is a **reference**, and it splits evals in two
([Confident AI](https://www.confident-ai.com/blog/why-llm-as-a-judge-is-the-best-llm-evaluation-method),
[Kerno](https://www.kerno.io/blog/llm-as-a-judge-evaluating-output-without-a-ground-truth)):

- **Reference-based** — you *have* the expected output; the judge compares
  against it. (Math answer, known-correct fixture.)
- **Reference-free** — no gold answer; the judge scores against a **rubric**.

**Open-ended code work — like a refactor — is almost always reference-free.**
There's no single "what it's supposed to look like," so you *replace the reference
with a rubric*. That's the whole reason rubrics exist.

### Wrong context is the top failure mode

> *"Major pitfalls include … feeding the judge the wrong inputs (e.g. a
> hallucination eval without retrieved context)."* ([Evidently](https://www.evidentlyai.com/llm-guide/llm-as-a-judge),
> [Arize](https://arize.com/llm-as-a-judge/))

The rule is **not** "send everything" or "send the minimum" — it's **send exactly
what *this* criterion needs to judge, and nothing that just adds noise.** Note
this is the same per-check heterogeneity as ADR 0003's "no universal input": the
criterion defines the input.

---

## 5. Offline evaluation vs. online guardrails

The sharpest axis, and the one that separates two activities the field
deliberately keeps apart. Two ways to ask "is this good?":

| | **Evaluating the agent** | **Evaluating a piece of work** |
|---|---|---|
| Field name | **offline evaluation** / "evals" / benchmarking | **online evaluation** + **guardrail** |
| **Subject under test** | the **agent** (its prompt/config) — reusable | a **single output** — one-time artifact |
| When | dev / tuning time, batch, no time pressure | runtime, in the request, sub-second budget |
| Data | a **curated dataset** of many examples | **this one** input/output |
| Ground truth | **reference-based** (known/golden outputs) | **reference-free** (none exists) |
| Output | an **aggregate** (pass rate, mean) | a **per-instance** verdict |
| Consequence | **none to the artifact** — you change the *agent* | **acts on the artifact** — warn / block / retry / refactor |
| Consumer | *you*, comparing prompt v1 vs v2 | *the running flow*, deciding next step |
| ML analogy | a **test / validation set** | a **runtime assertion / monitor** |

The literature is explicit:

> *"Evaluations run the system on a set of pre-prepared examples … a threshold of
> results is expected, whereas guardrails are tests performed as part of each
> request, at runtime."* ([Lior Bar-On](https://medium.com/@baronlior/llm-systems-testing-processes-aka-evaluations-b604924d97f5))
>
> *"Offline evals validate known scenarios against reference outputs, while online
> evals monitor production traces … without ground truth."* ([Deepchecks](https://deepchecks.com/question/online-vs-offline-llm-evaluation/),
> [Rhesis AI](https://rhesis.ai/post/offline-vs-online-evaluation-llm-applications))

### The single discriminator

> **What is the subject under test, and is there a consequence to the artifact?**

- Subject = the **reusable agent**, no consequence to any one output → you must
  **sample over many tasks and aggregate** → **offline eval**.
- Subject = **one artifact**, with a consequence → **judge that instance and act**
  → **online guardrail**.

Everything else in the table (dataset vs. single, references vs. none, aggregate
vs. per-instance) falls out of those two facts.

### Combined, they're a safety net

> *"Offline tests catch regressions before code reaches staging, while online
> monitors surface drift, abuse, and cost spikes in real time."* Use lightweight
> checks online for immediate guardrails; heavier LLM-based evals offline or on a
> delay. ([Deepchecks](https://deepchecks.com/question/online-vs-offline-llm-evaluation/),
> [Confident AI — guardrails](https://www.confident-ai.com/blog/llm-guardrails-the-ultimate-guide-to-safeguard-llm-systems))

### Separate the two verbs: grade vs. react

"Grade it **and react upon it**" is two things. The **grade** (is it OK?) is the
*online evaluation*; the **react** (warn/refactor/block/retry) is the *guardrail
action*. Keep the grader pure (verdict only); **the reaction is the flow's job.**
That seam is what lets the same grader drop into the offline eval untouched
(where there is no reaction at all).

---

## 6. Validated real-world examples (verbatim)

Pulled from respected / high-star / enterprise sources. Two buckets: **graders**
(output = a grade) and **reviewers** (output = findings).

### 6.1 OpenAI Evals — `closedqa.yaml` (the canonical rubric grader)

`openai/evals` is *the* reference eval framework (>10k★); this is the template
their own docs say to copy for any rubric grade.

```
You are assessing a submitted answer on a given task based on a criterion. Here is the data:
[BEGIN DATA]
***
[Task]: {input}
***
[Submission]: {completion}
***
[Criterion]: {criteria}
***
[END DATA]
Does the submission meet the criterion? First, write out in a step by step manner your
reasoning about the criterion to be sure that your conclusion is correct. Avoid simply
stating the correct answers at the outset. Then print only the single character "Y" or "N"
(without quotes or punctuation) on its own line corresponding to the correct answer. At the
end, repeat just the letter again by itself on a new line.

Reasoning:
```
```
eval_type: cot_classify
choice_strings: YN
choice_scores: { "Y": 1.0, "N": 0.0 }
```

**Lessons (every one of §1–§4 is in this file):** triple input
`{input}`+`{completion}`+`{criteria}`, **reference-free**; **binary** Y/N →
1.0/0.0; `cot_classify` = reason then label (rationale-as-exhaust enforced;
prints the letter twice for trivial parsing); **one criterion per call**.
Source: [closedqa.yaml](https://github.com/openai/evals/blob/main/evals/registry/modelgraded/closedqa.yaml),
[eval-templates docs](https://github.com/openai/evals/blob/main/docs/eval-templates.md).

### 6.2 Braintrust `autoevals` — `factuality.yaml` (reference-*based* grader)

Braintrust is a widely-used enterprise eval platform; `autoevals` is their OSS
scorer library; this is a hardened port of OpenAI's `fact.yaml`.

```
You are comparing a submitted answer to an expert answer on a given question. Here is the data:
[BEGIN DATA]
************
[Question]: {{input}}
************
[Expert]: {{expected}}
************
[Submission]: {{output}}
************
[END DATA]

Compare the factual content of the submitted answer with the expert answer. Ignore any
differences in style, grammar, or punctuation.
The submitted answer may either be a subset or superset of the expert answer, or it may
conflict with it. Determine which case applies. Answer the question by selecting one of the
following options:
(A) The submitted answer is a subset of the expert answer and is fully consistent with it.
(B) The submitted answer is a superset of the expert answer and is fully consistent with it.
(C) The submitted answer contains all the same details as the expert answer.
(D) There is a disagreement between the submitted answer and the expert answer.
(E) The answers differ, but these differences don't matter from the perspective of factuality.
```
```
choice_scores: { A: 0.4, B: 0.6, C: 1, D: 0, E: 1 }
```

**Lesson — the reference-based variant.** `[Expert]: {{expected}}` is present.
Instead of a free numeric score it forces the judge into **discrete labelled
buckets (A–E)** and the *developer* assigns numbers via `choice_scores` — the
field's answer to noisy numbers: **let the model classify, let the code score.**
The buckets encode real judgment (subset 0.4, superset 0.6, exact 1.0, stylistic
diff 1.0). Source: [factuality.yaml](https://github.com/braintrustdata/autoevals/blob/main/templates/factuality.yaml).

### 6.3 Ragas — `faithfulness` (decompose-then-grade, the fan-out pattern)

The de-facto RAG-eval library (>5k★).

1. **Decompose** — *"Given a question and an answer, analyze the complexity of
   each sentence … Break down each sentence into one or more fully understandable
   statements. Ensure that no pronouns are used … Format the outputs in JSON."*
2. **Grade each atom (NLI)** — *"Your task is to judge the faithfulness of a
   series of statements based on a given context. For each statement you must
   return verdict as 1 if the statement can be directly inferred based on the
   context or 0 if the statement can not be directly inferred based on the
   context."*

Final grade = fraction of statements that scored 1.

**Lesson — this is "split into many binaries and infer the grade", validated and
shipped.** Atomize → binary-per-atom → the **score is arithmetic, not vibes.**
Pronoun-removal is a real detail: a claim must be judgeable in isolation. Source:
[_faithfulness.py](https://github.com/explodinggradients/ragas/blob/main/src/ragas/metrics/_faithfulness.py).

### 6.4 Qodo (CodiumAI) PR-Agent — `pr_reviewer_prompts.toml` (production reviewer)

One of the most-used OSS AI code reviewers (operated commercially as Qodo Merge —
genuinely enterprise-grade). A production reviewer prompt, not a demo.

- **Role:** *"PR-Reviewer, a language model designed to review a Git Pull
  Request."*
- **Scope rule (the gold):** focus on **new code (lines starting with `+`)**; the
  diff is split into `__new hunk__` / `__old hunk__`; *"you only see changed code
  segments, not the entire codebase, so avoid suggesting to duplicate
  functionality or questioning code elements that may be defined elsewhere."*
- **Finding quality bar:** *"Each issue must be discrete and actionable, not a
  vague concern about the codebase in general"*; *"Be direct about why something
  is a problem and the realistic scenario where it manifests."*
- **Calibrated severity:** flag clear bugs/security thoroughly even if narrow;
  lower-severity only with confident, concrete explanations; don't flag
  intentional design/style; high-impact-but-uncertain → report *with an explicit
  uncertainty note*.
- **Output = structured findings (YAML):** `key_issues_to_review` (0..N, each with
  `relevant_file`, `issue_header`, `issue_content`, `start_line`, `end_line`),
  `security_concerns`, `relevant_tests`, `todo_sections`, and a single
  `score: 0-100`.

**Lesson — grader/reviewer made concrete in one artifact.** The bulk is
`key_issues_to_review` (locations + actions) = a **reviewer**; it *also* emits a
single `score` = the **grade projection** bolted on. Same engine, both outputs —
exactly "review ⊇ grade", and even here the load-bearing part is the findings.
The **scope discipline** ("you only see the diff, don't flag what might be defined
elsewhere") is the production-hardened answer to the input question: feed the
diff, *and warn it that it's only the diff* to suppress false positives. Source:
[pr_reviewer_prompts.toml](https://github.com/qodo-ai/pr-agent/blob/main/pr_agent/settings/pr_reviewer_prompts.toml).

### 6.5 Anthropic — Constitutional AI critique→revise (research-grade critique loop)

The canonical formulation of LLM self-critique.

- **Critique request:** *"Identify specific ways in which the assistant's last
  response is [harmful / unethical / …per the principle]."*
- **Revision request:** *"Please rewrite the assistant response to remove any and
  all [harmful/…] content."*

Runs a fixed set of **principles one at a time**, each producing a critique → a
revision.

**Lesson — the purest reviewer/critic.** Output is *feedback meant to drive a
fix*, not a score, and its quality is judged by **downstream improvement** — the
literature's definition of a good critique vs. a good grade. Mirrors "one rule per
pass" on the reviewing side. Source:
[Constitutional AI: Harmlessness from AI Feedback](https://www.anthropic.com/research/constitutional-ai-harmlessness-from-ai-feedback).

---

## 7. Cross-cutting patterns worth stealing

| Pattern | Seen in | Takeaway |
|---|---|---|
| Triple input: task + output + (optional) reference/criteria | OpenAI closedqa, Braintrust factuality | There *is* a standard input schema; reference is optional |
| Reference-free → carry a rubric instead | closedqa (`{criteria}`) | Open-ended work (refactors) = rubric, not gold answer |
| **Classify, don't score — code assigns the number** | Braintrust A–E + `choice_scores` | Keeps the LLM out of noisy numeric estimation |
| Reason first, label last, label twice | closedqa `cot_classify` | Rationale is exhaust; the parseable grade is the contract |
| Atomize → binary-per-atom → arithmetic score | Ragas faithfulness | "Many binaries → infer the grade" is the validated method |
| One criterion / principle per pass | closedqa, Constitutional AI | Single-criterion judges; compose, don't build a mega-judge |
| Tell the judge the scope of what it can see | Qodo (`+` lines, hunks, "defined elsewhere") | Suppresses false positives; answers "what context to send" |
| Grade is a projection bolted onto a reviewer | Qodo (`key_issues` + `score`) | Build the reviewer; derive the grade — never the reverse |

---

## 8. The anchoring spectrum (the unifying idea)

Across the three worked examples in [`examples/`](examples), the grader atom
never changes (`judge → verdict`). What changes is **what anchors the judgment**:

| Use case | Anchor | Activity | Reaction |
|---|---|---|---|
| [Rule check](examples/01-rule-guardrail.md) | a **rule** | online guardrail | yes — refactor |
| [Similarity check](examples/02-similarity-eval.md) | a **reference** | offline eval | none — tweak + rerun |
| [Black-box quality](examples/03-blackbox-quality-eval.md) | **nothing but the rubric** | online or offline | depends |

**Less anchor → more burden on the rubric, and more need to evaluate the
evaluator.** That is the real spectrum an eval layer must span. Reference-free is
the demanding end: the rubric must be **evidence-anchored** (force the judge to
quote its support) *and* the judge must be **validated against human labels**,
because nothing else keeps it honest.

---

## 9. Implications for our build (parked, not decided)

These connect the research to the orchestrator's actual seams (ADR 0003). They are
**observations for a future plan**, not commitments.

- **The judgment is naturally an Actor.** It has intent + identity (a rubric /
  judge model), is configured once, exposes a typed verb, and each call is a
  tracked unit → mints an `IOperation<Verdict>`. It is a *sibling* to `Agent` /
  `Validator`, may *compose* an `Agent` for LLM-as-judge, and shares no base
  beyond the verdict shape (no `IEvaluator`).
- **Subject under test maps onto our nouns.** Evaluating the **agent** = evaluating
  the **Actor** (sample over a fixture suite, aggregate — offline). Evaluating a
  **piece of work** = evaluating an **Operation result** (judge inline, maybe
  loop back — online guardrail).
- **The offline side is blocked on the trace seam.** To evaluate the agent over
  past runs you need recorded inputs/outputs; today `OperationRecord` carries only
  a summary string. Flow-level and agent-tuning eval are the forcing function that
  finally cashes in ADR 0003 §4's reserved trace seam. The inline guardrail has
  the artifact in hand and needs no such thing.
- **Build the check once, wrap it twice.** One single-criterion binary grader;
  an *eval harness* (`dataset × check → aggregate`) when the subject is the agent,
  a *guardrail harness* (`one artifact × check → verdict → action`) when the
  subject is the output.
- **Keep grade and react on opposite sides of a seam.** Grader returns a verdict;
  the flow decides the reaction. (YAGNI: if "auto-evaluate every operation" ever
  arrives, its one home is the `Flow.Run` seam after `CompleteOperation` — but
  build the explicit inline gate first.)

---

## Sources

**Concept — grading, judges, rubrics**
- [Confident AI — Why LLM-as-a-Judge is the best evaluation method](https://www.confident-ai.com/blog/why-llm-as-a-judge-is-the-best-llm-evaluation-method)
- [Braintrust — What is an LLM-as-a-judge? (vs. deterministic)](https://www.braintrust.dev/articles/what-is-llm-as-a-judge)
- [Braintrust — LLM-as-a-judge vs. human-in-the-loop](https://www.braintrust.dev/articles/llm-as-a-judge-vs-human-in-the-loop-evals)
- [Future AGI — LLM evaluation frameworks & best practices](https://futureagi.com/blog/llm-evaluation-frameworks-metrics-best-practices/)
- [Masood (2026) — Rubric-based evals & LLM-as-a-judge: methodologies & biases](https://medium.com/@adnanmasood/rubric-based-evals-llm-as-a-judge-methodologies-and-empirical-validation-in-domain-context-71936b989e80)
- [RULERS — Locked rubrics & evidence-anchored scoring (arXiv 2601.08654)](https://arxiv.org/pdf/2601.08654)
- [Arize — The Definitive Guide to LLM Evaluation](https://arize.com/llm-evaluation/)
- [Evidently — LLM-as-a-judge: a complete guide](https://www.evidentlyai.com/llm-guide/llm-as-a-judge)
- [Kerno — LLM-as-a-Judge: evaluating without ground truth](https://www.kerno.io/blog/llm-as-a-judge-evaluating-output-without-a-ground-truth)

**Concept — critique vs. evaluation**
- [RealCritic — effectiveness-driven evaluation of critiques (arXiv 2501.14492)](https://arxiv.org/pdf/2501.14492)
- [CritiqueLLM — informative critique generation (arXiv 2311.18702)](https://arxiv.org/pdf/2311.18702)
- [When Your Reviewer is an LLM — biases & divergence in peer review (arXiv 2509.09912)](https://arxiv.org/pdf/2509.09912)

**Concept — offline vs. online / guardrails**
- [Deepchecks — Online vs. Offline LLM evaluation](https://deepchecks.com/question/online-vs-offline-llm-evaluation/)
- [Rhesis AI — Offline vs. online evaluation: a practical guide](https://rhesis.ai/post/offline-vs-online-evaluation-llm-applications)
- [Lior Bar-On — LLM systems testing (evaluations vs. guardrails)](https://medium.com/@baronlior/llm-systems-testing-processes-aka-evaluations-b604924d97f5)
- [Confident AI — LLM Guardrails: the ultimate guide](https://www.confident-ai.com/blog/llm-guardrails-the-ultimate-guide-to-safeguard-llm-systems)

**Shipped prompts / code (verbatim)**
- [openai/evals — closedqa.yaml](https://github.com/openai/evals/blob/main/evals/registry/modelgraded/closedqa.yaml) · [fact.yaml](https://github.com/openai/evals/blob/main/evals/registry/modelgraded/fact.yaml) · [eval-templates](https://github.com/openai/evals/blob/main/docs/eval-templates.md)
- [braintrustdata/autoevals — factuality.yaml](https://github.com/braintrustdata/autoevals/blob/main/templates/factuality.yaml)
- [explodinggradients/ragas — _faithfulness.py](https://github.com/explodinggradients/ragas/blob/main/src/ragas/metrics/_faithfulness.py)
- [qodo-ai/pr-agent — pr_reviewer_prompts.toml](https://github.com/qodo-ai/pr-agent/blob/main/pr_agent/settings/pr_reviewer_prompts.toml)
- [Anthropic — Constitutional AI: Harmlessness from AI Feedback](https://www.anthropic.com/research/constitutional-ai-harmlessness-from-ai-feedback)
</content>
</invoke>
