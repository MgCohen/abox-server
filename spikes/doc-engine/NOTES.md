# Spike findings — doc-engine

What the spike proved, what it punted, and the decisions to make before this
becomes real infrastructure.

## What it proved

- **Data-defined catalogs work.** 7 lean block schemas + 1 doc-type catalog, no
  code, fully drove validation of a real distilled plan.
- **Quality rules become enforcement, not rubric.** "Never a single-step plan"
  is `phase: { min: 2 }`; the status vocabulary is an `enum`; "one open-questions
  block, last" is `{ max: 1, position: last }`. The validator caught all three
  when broken (bad enum, unknown attr, min-occurrence). This is the AI-first
  payoff over BuilderIO's prose-only rubrics.
- **Distill ≠ transcribe.** The 323-line dump → 17 blocks that stand alone. The
  `status=blocked` phase carries its blockers inline; no separate progress layer.

## Rubric vs enforcement (the line)

| Concern | Where | Hard or soft |
|---|---|---|
| block exists / attrs typed / enum values | block YAML | **hard** (validator) |
| required blocks, min/max, position | doctype YAML | **hard** (validator) |
| "distill don't transcribe", "name real files", "lead with reuse" | `rubric:` text | soft (author guidance) |

Soft rubric is for the LLM author; hard structure is for the validator. Keep
genuinely-prose things (a summary's narrative) soft — over-structuring breeds
filler.

## Punted (decide before promoting out of spike)

1. **Structured list fields.** Kept everything to `scalar attrs + markdown body`
   (lean, per feedback). `key-files` / `references` as typed lists
   (`[{path, note}]`) were *not* modeled. Do we need machine-readable lists, or
   is markdown-in-body enough? Probably add 1–2 list blocks only when a consumer
   needs them.
2. **Blocker / precondition block.** The dump's owner-escalation preconditions
   were folded into `phase.status=blocked` + body. A first-class `blocker` block
   (owner-action, severity) is a candidate — but YAGNI until a second doc type
   wants it.
3. **Meta-schema.** `validate.py` hard-codes the field vocabulary (`type`,
   `enum`, `values`, `min`, `max`, `position`). Next step is a meta-schema that
   validates the `blocks/*.yaml` and `doctypes/*.yaml` themselves, so a typo in a
   definition fails on load (the type-checker equivalent we lose by going to YAML).
4. **Order enforcement.** Only `position: first|last` is enforced; full block
   ordering is not. Add a `order:` list to the doctype if strict sequence matters.
5. **The selector.** This spike hand-distilled the dump. The real engine needs
   the **author prompt** that turns dump + catalog → conformant blocks, then runs
   `validate.py` as its own gate (self-correct on failure).

## How this lands in the repo (when real)

- Generic engine (registry, parser, validator) → `Core` (generic infra).
- Validator runs as a **Rulebook-style structure guard** in CI — same posture as
  `tests/Tests/Structure`. A non-conforming doc fails the build, like a test that
  lands without its Rule.
- `blocks/` + `doctypes/` are the data; adding a doc type (ADR, research-note,
  recap) is a YAML change, reusing the same engine.
