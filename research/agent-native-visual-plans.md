# Building a "Visual Plan" System — Extracted Pattern & Reference

Reverse-engineered from `BuilderIO/skills` (`/visual-plan`, `/visual-recap`,
`agent-watchdog`, `plan-arbiter`) and `BuilderIO/agent-native`. This is the
generic logic plus the actual reviewer prompts/personas to borrow.

---

## 1. The core pattern (the one idea)

> **Don't have the LLM produce the final artifact. Have it produce structured
> data that a deterministic renderer turns into the artifact.**

Three roles, sharing nothing but a **schema**:

```
        ┌─────────────── SCHEMA / BLOCK CATALOG ───────────────┐
        │   block types + required fields + authoring rules      │   (the contract)
        └────────┬──────────────────────────────────┬───────────┘
                 │ read                                │ read
        ┌────────▼────────┐                  ┌─────────▼─────────┐
        │     AUTHOR        │                 │     RENDERER        │
        │ (LLM / agent)     │ ── Block[] ──►  │ (server / registry) │
        │ source → blocks   │   (the IR)      │ blocks → visual page │
        └─────────────────┘                  └───────────────────┘
                 ▲                                     │
                 └──────────── feedback ◄──────────────┘
                       (human comments, anchored)
```

- **Author** = the LLM. Good at judgment ("what matters in this PR?"). Emits typed blocks.
- **Schema** = the block catalog. The renderer's vocabulary, fetched at runtime so the LLM never authors from memory.
- **Renderer** = deterministic code. Owns layout, theme, quality. Turns `Block[]` into an interactive, commentable page.
- **Feedback loop** = humans comment on the rendered page; comments are anchored to specific blocks/elements; the agent reads them back and emits *patches*, not rewrites.

Why it wins:
- Author and renderer evolve independently (new renderer without retraining the agent; new block type without touching the viewer).
- The LLM does only what it's good at; the renderer guarantees consistent quality.
- Same IR → many outputs (HTML, Markdown, JSON, PDF). This is the "define once, render everywhere" thesis.

---

## 2. The actual loop (from `/visual-plan` Core Workflow)

1. **Research first** — read real files, schema, existing patterns. Name actual files/symbols, don't invent. *(Planning is read-only — no source edits.)*
2. **`get-plan-blocks`** — fetch the authoritative catalog. Never author tags from memory.
3. **`create-*-plan`** — emit blocks via the mode-matched create tool → backend renders → returns a URL.
4. **Surface the URL** — ask the human to review. (The plan IS the approval gate.)
5. **`get-plan-feedback`** — pull anchored human comments back.
6. **`update-visual-plan`** — apply targeted `contentPatches` (never a partial full-replacement).
7. **`export`** — only when a shareable receipt/repo artifact is wanted.

In between 3 and 4, on high-stakes plans, an **adversarial self-review pass** runs (see §6).

---

## 3. Components to build your own

| Component | What it is | BuilderIO's version |
|---|---|---|
| **Block schema / catalog** | Typed blocks: `type`, required fields, prop shapes, authoring rule | live registry via `get-plan-blocks` |
| **Catalog endpoint** | Lets the agent fetch the schema at runtime (no-auth, no content) | `get-plan-blocks` / `plan blocks` CLI |
| **Create/author tools** | Agent submits `Block[]`; server validates + persists + renders | `create-visual-plan`, `create-ui-plan`, … |
| **Renderer registry** | `Map<blockType, renderFn>` + a bounded fallback (`custom-html`) | the Plan app |
| **Persistence + IR** | Canonical runtime JSON; repo-friendly MDX as export/authoring | JSON runtime, `plan.mdx`/`canvas.mdx` |
| **Anchored feedback** | Comments tied to block id / node id / text quote / coords | `get-plan-feedback` with rich anchors |
| **Patch tools** | Surgical edits by stable id, not regeneration | `contentPatches`, `patch-*-html` |
| **Host modes** | hosted SaaS / local-files bridge / self-hosted | `plan.agent-native.com`, local bridge |

### Block types worth stealing (their catalog)
`rich-text` · `diagram` (HTML/CSS/SVG) · `data-model` · `api-endpoint` · `diff` ·
`file-tree` · `code` · `annotated-code` (margin notes anchored to line ranges) ·
`table` · `checklist` · `callout` (`tone="decision"`) · `columns` (before/after) ·
`tabs` · `question-form` (open questions, with `recommended` defaults) ·
`custom-html` (bounded escape hatch only).

### Two surfaces (their separation of concerns)
- **Canvas** = static UI wireframes/artboards laid out in lanes, with plain-text designer annotations anchored by `targetId` + `placement`.
- **Document** = the technical depth the visuals can't show (file maps, contracts, risks, verification).
- Rule: **canvas and document never duplicate each other.** UI work → story lives in canvas; architecture work → no canvas, inline `diagram` next to each claim.

### Key design decisions to copy
- **Schema fetched at runtime**, not hardcoded in the prompt → renderer is the single source of truth, agent can't drift.
- **IR is canonical JSON; MDX is the export/authoring surface** → git-friendly without coupling the renderer to files.
- **Patches over regeneration** → `content` is a *full replacement*; partial sends silently drop blocks, so edits go through id-addressed patch ops.
- **Anchored comments** → every comment carries coordinate frame + node id + text quote, so the agent knows exactly what each comment points at, and flags `detached` comments instead of dropping them.
- **No inline fallback** → if the renderer is unreachable, STOP and fix the connection; never degrade to chat-only output. Quality complaints fix the *renderer or the skill*, never one stored artifact.

---

## 4. The authoring quality bar (reference: `document-quality.md`)

These are the rules that make the *content* good, not the *rendering*. Borrow the strategy:

- **Serious technical plan, not marketing.** Outcome-first, prose-first, self-contained, specific. State objective + "done" + scope/non-goals + approach + key decisions w/ rationale + ordered steps naming real files/symbols + risks + a verification step. *"Replace vague prose with specifics; never ship a step like 'make it work.'"*
- **Every artifact stands alone.** No changelog-of-the-conversation language ("preserve the previous plan", "this revision", "as discussed above"). A reader who never saw the chat must understand it.
- **Make abstract things instantly legible.** Lead with one concrete example/snapshot before dense architecture or mode tables.
- **Preserve the user's level of abstraction.** A motivating example is not automatically the architecture — separate the reusable core from app-specific adapters and future examples.
- **Use the right block, make it carry substance.** Prefer `annotated-code` over bare `code` for load-bearing files; a tab that reveals only prose means the plan is under-specified.
- **Open questions live in ONE bottom `question-form`** — never a second questions wall earlier; `recommended: true` on the default; always allow a write-in.
- **Verification must exercise the real workflow** — at least one end-to-end smoke matching the user journey, not just typecheck/unit.
- **`custom-html` is a bounded escape hatch only** — never the primary home for a mockup; if fidelity needs it, extend the renderer instead.
- **Before handoff, open it and check it** — fix overlap, clipping, contrast, unreadable diagrams before asking for approval.

---

## 5. The "good vs. bad" exemplar trick (reference: `exemplar.md`)

They ship a canonical **worked example + explicit anti-patterns** the agent reads before authoring. This is a cheap, high-leverage way to pin quality. Pattern:

- **GOOD**: a concrete, fully-described instance of the bar ("a UI-first plan for a todo app: a canvas with a `desktop` artboard whose `data.html` is a real flex layout … below it a Claude/Codex-grade document: objective and done-criteria, `code` blocks … a `callout` with `tone='decision'` … a validation step — none of it repeating the canvas. This is the bar.").
- **BAD**: an enumerated list of concrete failures to never produce (hard-coded hex colors, placeholder gray bars, a marketing hero heading restating the canvas, an architecture-only plan forced into a top canvas of overlapping labeled boxes, a plan that describes itself as a revision…).

Takeaway for your own system: **don't just give rules — give one gold example and a blacklist of named anti-patterns.**

---

## 6. The reviewer / validator prompts (what you asked for)

The repo has *three distinct review personas*. These are directly reusable as system prompts for a validation agent.

### 6a. In-line self-review before handoff (from `/visual-plan`)

> For high-stakes plans — architecture, backend, data-model, migration, multi-file,
> or otherwise risky work — run one adversarial self-review pass before treating the
> plan as final. Skip it for small, UI-only, or single-decision plans …
>
> - **Surface the plan first, review concurrently.** Post the link and let the user start reading, then run the review in parallel — never make the user wait on it.
> - **Review the written plan; do not re-research.** Critique the plan text and its own blocks. The grounding was already done while drafting, so the review checks the output instead of re-exploring the repo.
> - **Spawn one skeptical reviewer** whose only job is to find what is weak, missing, or wrong — not to praise. Point it at: hard-to-reverse decisions made implicitly or not at all (wire format, public ids, data-model shape, auth, ownership); steps not anchored in real files or symbols; a menu of options where the plan should commit to one; obvious missing decisions ("what happens when X?", "why not Y?"); and padding or single-step filler.
> - **Fix vs. ask.** Apply clear-cut fixes yourself … Route genuine judgment calls back to the user instead … Do not silently decide them.
> - **Do not surprise the user mid-read.** … summarize what the review changed and what it surfaced for the user to decide.

**Strategy distilled:** non-blocking, output-only critique, single skeptic persona, fix-the-obvious / escalate-the-judgment, never silently decide.

### 6b. Auditing another agent's work (`agent-watchdog`)

Persona: *"Watch another agent's work like a reviewer with a pager."* Steps:

1. **Reconstruct the contract** — original request, explicit constraints, *implied* acceptance criteria, the other agent's claims/caveats. *"Treat the user's request as the source of truth, not the other agent's summary."*
2. **Audit evidence, not vibes** — read changed + nearby unchanged files, real git diff, compare claimed commands vs. actual output, inspect failed/skipped tests + CI + screenshots.
3. **Classify each issue**: `Gap` (missing behavior) · `Bug` (likely fails/regresses) · `Verification miss` (maybe right, weak evidence) · `Scope drift` (unrelated change / skipped constraint) · `No issue` (handled, with evidence).
4. **Fix narrowly** (only if authorized) — clear-cut gaps only, preserve unrelated work, smallest useful validation after each fix; stop and report if a fix needs a product decision / credential / broad rewrite.
5. **Report** with a fixed template: `Status / Requested / Observed / Gaps / Fixes made / Remaining risk`.

### 6c. Arbitrating competing plans (`plan-arbiter`)

Persona: *"Turn competing plans into one executable direction … instead of a blended mush."*

- **Normalize** each plan into comparable claims (objective, assumptions, files/APIs, sequence, validation, rollback, cost/executor-fit). *"Do not reward verbosity. Prefer plans that are concrete, grounded in real code, and honest about tradeoffs."*
- **Cross-review** each as if a capable peer wrote it; find hidden deps, missing tests, risky sequencing, vague steps, scope creep, hard-to-reverse decisions; separate **plan quality from executor preference**.
- **Decide**: `Adopt` / `Hybrid` / `Revise first`, with this tie-break order:
  1. Correctness & fit to the request
  2. Grounding in real files/APIs/tests/data/UI
  3. Simpler first implementation that doesn't block the future
  4. Better validation & rollback story
  5. Lower token/time cost once quality is acceptable
- **Handoff** with a fixed memo: `Decision / Why / Execution Plan / Borrowed From Other Plans / Rejected / Verification / Executor Recommendation`.

**Common thread across all three personas:** reconstruct the real contract → verify against ground truth (code/tests/CI), not summaries → classify findings with a fixed taxonomy → fix only the clear-cut, escalate the judgment calls → report in a fixed scannable template.

---

## 7. Minimal recipe to build your own

1. **Define blocks as a schema** (Zod / JSON Schema / TS types). Each block: `type`, fields, and a one-line *authoring instruction* the agent reads.
2. **Expose the catalog** via a no-auth `get-blocks` tool/route (sends schema only, no content).
3. **Author step**: agent researches → emits `Block[]` → validate against schema → repair/reject on invalid.
4. **Renderer** = `Map<type, renderFn>` + bounded fallback. Persist canonical JSON; offer MDX export for git.
5. **Add the loop**: render to a commentable surface → anchor comments by block id / node id / text quote → agent reads feedback → emits **id-addressed patches**, not rewrites. Flag detached comments.
6. **Add a validator agent** using the §6 personas: a skeptic that reviews the *output* (not re-research), classifies with a fixed taxonomy, fixes the obvious, escalates judgment, reports in a fixed template.
7. **Pin quality** with a `document-quality` rulebook + one GOOD exemplar + a named anti-pattern blacklist, all read before authoring.
8. **Offer host modes**: hosted (shareable+comments), local-files (bridge, no DB writes), self-hosted.

---

## Source files (verbatim, in `BuilderIO/skills`)
- `skills/visual-plan/SKILL.md` — workflow, plan discipline, self-review, tool guidance
- `skills/visual-plan/references/document-quality.md` — content quality bar
- `skills/visual-plan/references/canvas.md` — surface/artboard/annotation mechanics
- `skills/visual-plan/references/exemplar.md` — good/bad worked example
- `skills/visual-recap/SKILL.md` — same data model, run backwards from a diff
- `skills/agent-watchdog/SKILL.md` — audit persona + report template
- `skills/plan-arbiter/SKILL.md` — compare/arbitrate persona + decision memo
