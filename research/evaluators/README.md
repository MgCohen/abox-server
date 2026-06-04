# Evaluators — research & worked examples

> Conceptual research for an eval/guardrail layer in the orchestrator (the
> natural layer after the flow). **Concept only — no implementation decided.**
> Captured 2026-06-03.

## Read in this order

1. **[01-evaluation-and-grading-research.md](01-evaluation-and-grading-research.md)**
   — the full research. What an evaluator is, grade shape, deterministic vs.
   LLM-judge, **grader vs. reviewer**, the grader's input, **offline-eval vs.
   online-guardrail**, validated real-world prompts (OpenAI Evals, Braintrust,
   Ragas, Qodo PR-Agent, Constitutional AI), cross-cutting patterns, and parked
   implications for our build. Fully cited.

2. **Worked examples** (pseudocode + made-up context; not our project's types):
   - **[examples/01-rule-guardrail.md](examples/01-rule-guardrail.md)** — online
     guardrail: judge one artifact against a *rule*, then react. Shows the
     deterministic + LLM "hybrid norm".
   - **[examples/02-similarity-eval.md](examples/02-similarity-eval.md)** — offline
     eval: run an agent over a *dataset*, grade against a *reference*, aggregate.
     The prompt-tuning loop.
   - **[examples/03-blackbox-quality-eval.md](examples/03-blackbox-quality-eval.md)**
     — reference-free quality eval: no rule, no reference, rubric carries
     everything. Evidence-anchoring + judge-validation.

## The one-paragraph summary

An evaluator is `input → check → grade`. The check is deterministic *or* a simple
single-criterion binary LLM judge (use both — the "hybrid norm"). The **grade is
the contract; the rationale is exhaust.** A **reviewer** is the same engine that
*retains its findings* instead of projecting them to a scalar (review ⊇ grade).
Two activities the field keeps apart: **offline evaluation** (subject = the agent;
dataset + references; aggregate; tunes prompts) vs. **online guardrails** (subject
= one output; runtime; reference-free; acts on the artifact) — discriminate by
*subject under test + consequence*. The less you anchor a judge (rule → reference →
rubric), the more the rubric carries and the more you must validate the judge
itself against human labels. Build the check **once**; wrap it as an eval or a
guardrail.
</content>
