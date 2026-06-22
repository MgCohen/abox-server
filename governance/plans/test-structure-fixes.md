# Test-structure fixes

Action items from the code-quality review of the Rulebook test harness.
Each: **what / why / change / expected**. Ordered by priority.

---

## Foundational layout decision: the two-file Rulebook layout — underpins #3, #7, #8, #9, #10

Each `<Type>/Rulebook/` holds **two files**, neither needing anything "ignored":

```
tests/Tests/<Type>/Rulebook/
  template.md   the per-type schema: one rule's worth of shape (header + bold **Why:**)   → read by #9
  rules.md      a preamble stub (title + one-liner + README pointer) then the ### rules    → read by ParityGuard + #10
```

The shared convention / parity mechanism / canonical skeleton lives once in `Harness/README.md` (#7/#8).
Because the template no longer sits inside `rules.md`, **no `### ` example collides with real rules — the
fence-skip is deleted** (#3) and there is nothing in either file to ignore or game.

---

## 1. Harness README: `[Fact]` → `[ParityFact]` (build-breaking trap)

- **What:** [`tests/Harness/README.md`](../../tests/Harness/README.md) teaches the parity fact as a plain `[Fact]` (§ "The two pieces", lines 27–33; § "Standing up a new type", step 3).
- **Why:** Real code uses `[ParityFact]` — the one marker exempt from `requireAllCited`. Follow the doc literally on a `requireAllCited` type (the example even uses the Arch path) and the build goes red on "uncited". The canonical convention doc hands you a failing build.
- **Change:** Both spots → `[ParityFact]`. Class name `Parity` → `ParityTests` to match reality.
- **Expected:** Copying the README example yields a green build.

## 2. Derive Rulebook path from anchor namespace

- **What:** All 6 `ParityTests` hardcode `Assert("<Type>/Rulebook/rules.md")`.
- **Why:** The path is fully derivable from `anchor.Namespace` (`ABox.Tests.Unit.Tests` → `Unit/Rulebook/rules.md`). Hardcoding it violates the harness's own "derive; don't hardcode what drifts" doctrine and leaves 6 literals to keep in sync with the folder.
- **Change:** `ParityGuard.Assert(rulebookPath = null)` defaults to a path derived from `anchor.Namespace`. Call sites become `For(typeof(ParityTests)).Assert()`.
- **Expected:** No path string at any call site. Folder rename → IDE0130 forces namespace rename → derivation tracks it; no manual literal edits.

## 3. Delete the fence-skip in `DeclaredRules` (moot under the two-file layout)

- **What:** [`ParityGuard.DeclaredRules`](../../tests/Harness/ParityGuard.cs) toggles `inFence` to skip the in-file `Template:` example so its `### ` isn't counted as a rule. A negative "ignore-this" mechanism: an unbalanced/stray fence silently hides real rules (parity passes green with enforcement gone — the silent-green failure the README calls most dangerous) and is gameable.
- **Why:** Under the two-file layout the template lives in `template.md`, so `rules.md` has no `### ` example — nothing to skip. Remove the *reason* to skip rather than guarding the skip.
- **Change:** Delete the `inFence` toggle + the fenced-skip branch; every `### ` in `rules.md` counts unconditionally. No "fail loud on unbalanced fence" guard is needed — there are no fences to balance.
- **Expected:** `rules.md` has no ignore mechanism; a stray fence cannot hide a rule because nothing is skipped.

## 4. Trim comment essays to the repo's one-line-why standard

- **What:** Multi-paragraph comments past CLAUDE.md's "one-line why" rule: [`TestMarkers.cs`](../../tests/Harness/TestMarkers.cs) (11 lines), [`ArchitectureModel.cs`](../../tests/Tests/Arch/Support/ArchitectureModel.cs) (several blocks), [`ParityGuard.cs`](../../tests/Harness/ParityGuard.cs) (6 lines).
- **Why:** `src/` was swept to no-comments; the test tree holds itself to a looser bar. Rejected-alternative rationale (name-list vs subtype-audit) is design-journal material — it already lives in commit `8637024`.
- **Change:** Trim to a one-line *why* + pointer (commit/ADR). Drop the rejected-alternative narration.
- **Expected:** Test tree matches the `src/` comment standard; no standing essays.

## 5. New-type docs omit the mandatory `TestTypes.Registered` step

- **What:** [`tests/Harness/README.md`](../../tests/Harness/README.md) § "Standing up a new test type" and [`tests/Tests/README.md`](../../tests/Tests/README.md) § "How to extend" list the new-type steps; neither says to add the type to `TestTypes.Registered`.
- **Why:** `StructureTests.EveryTestFolderIsARegisteredType` fails the instant the new folder lands if it's unregistered. Following the docs literally → red build. The gate is deliberate; the instructions to pass it are missing.
- **Change:** Add the `TestTypes.Registered` edit as an explicit step in both docs (alongside "no csproj edit needed").
- **Expected:** The documented flow produces a green build end to end.

## 6. One mandatory schema (header + bold `**Why:**`); no optional fields

- **What:** The `Why:` field is styled two ways (bold in Arch/Structure, plain in behavioral), and the Arch/Structure templates list fields (`How`/`Note`/`Companion`) that only *some* of their rules actually carry — 3 of 4 Arch rules have no `How`. A label that appears on some rules and not others is decoration, not a schema.
- **Why:** No optional fields. A field exists iff *every* rule of that type has it; anything rarer is a plain prose line under the rule (the repo's one-line-comment allowance), not a structured bullet. Run that through the data and the only universal field is `**Why:**`; the only legitimate per-type variation is the **header shape**. This makes the schema actually true and enforceable (enables #9), and converges the per-type templates.
- **Change:**
  - **Mandatory schema, all six:** `### <guarantee, in the type's header shape>` + bold `- **Why:** <…>`. That is the entire structured field set. Same styling everywhere.
  - **Only per-type difference is the header shape:** behavioral ends `→ <expected result>`; invariant reads `<subject> must/must not <relationship>`. Document the two shapes with one example each.
  - **Demote non-universal bullets to prose:** existing `**How:**`/`**Note:**`/`**Companion:**` bullets become plain lines under the rule, or drop if filler. No bold-label field beyond `Why`. (Re-wording an existing Rule is a deliberate edit per the stability contract — not a sweep.)
  - **Directness guard:** no editorial bullets, decision logs, or opinions in templates.
- **Expected:** One identical structured schema everywhere (header + `Why`); rare extras are prose comments; Arch/Structure rules migrated off multi-field bullets.

## 7. De-duplicate the parity-mechanism preamble (the two-file layout)

- **What:** The shared "`###` header IS the Rule; a `[Rule]` test enforces it; ParityTests keeps them in lockstep; Cardinality…" boilerplate is copy-pasted near-verbatim into all 6 Rulebooks.
- **Why:** "Reshape the convention" then = a 6-file edit — the exact operation the README flags as most dangerous. Trade-off given up: each Rulebook is currently self-contained — but #8's skeleton restores "where do I see the shape."
- **Change:** `rules.md` keeps only a **preamble stub**: title + a one-liner "what a Rule means here" + a pointer to the shared preamble. The template block leaves `rules.md` entirely (it becomes `template.md`, the two-file layout); the mechanism prose lives once in `Harness/README.md`.
- **Expected:** Convention shape has one home; `rules.md` is preamble-stub + rules, nothing duplicated.

## 8. Canonical Rulebook skeleton; new-type step "fills" it, not "copies a sibling"

- **What:** No template exists for the *Rulebook files themselves*. Both new-type docs say to *copy the preamble + template from a sibling* ([`tests/Harness/README.md`](../../tests/Harness/README.md) step 2; [`tests/Tests/README.md`](../../tests/Tests/README.md) "How to extend").
- **Why:** Copy-from-sibling is the drift source behind #6 (two `Why:` stylings) and #7 (6× preamble). File structure is defined by whichever sibling you happen to copy — no single source.
- **Change:** Put the canonical skeleton for **both files** — the `template.md` schema shape and the `rules.md` preamble stub — once in `Harness/README.md`. Change the new-type step from "copy a sibling" → "create `template.md` + `rules.md` from this skeleton." Closes the loop with #5/#6/#7.
- **Expected:** Both Rulebook files are fill-in-the-blanks against one owner, not archaeology across siblings.

## 9. Enforce that every Rule matches its type's `template.md` (format, not content)

- **What:** The template is aspirational — nothing checks that the rules follow it. A new test makes it a self-enforced schema. **Depends on #6** (single mandatory schema, no optional fields → exact match is possible) and the two-file layout (`template.md` is the schema source).
- **Why:** Closes organizational drift with zero content judgment: a rule missing `- **Why:**`, using plain `Why` instead of bold, carrying a stray bold-label bullet not in the schema, or dropping the `→` — all caught automatically. Complements ParityGuard (Rule ↔ test) with Rule ↔ schema. Self-scaling: a new test *type* (a 7th, 8th, …) is covered the moment its folder lands.
- **Change:** One **Structure** Rule ("Every Rule matches its type's template") + a test that globs every `*/Rulebook/`, reads `template.md` for the field set, and validates each `### ` rule in `rules.md` against it. No per-type wiring. The check: a rule's set of `- **Label:**` bullets **equals** the template's set (no optional fields → equality, not subset); plain prose lines are ignored (the comment allowance); header carries `→` iff the template header does. Never check placeholder content.
- **Expected:** Schema enforced across all Rulebooks; new types covered free; checking stays structural, not content-level.

## 10. Enforce that `rules.md` contains only rules (file grammar)

- **What:** Without this, #9 only validates the `### ` blocks it finds — a `## Scratch` section or a loose prose blob between rules slips in unchecked. This guards the file as a whole. **Depends on the two-file layout** (`rules.md` is rules-only by design; this enforces it).
- **Why:** Keeps `rules.md` from rotting into a dumping ground and completes the lockdown — ParityGuard (rule ↔ test) + #9 (rule ↔ schema) + #10 (file = only rules).
- **Change:** One **Structure** Rule ("Every Rulebook holds only rules") over each `rules.md`. Two regions: a bounded **preamble** before the first `### ` (title + one-liner + README pointer only), then **rule blocks** to EOF (only `### ` headers, their `**Why:**` bullet, and plain prose lines under a rule). 80/20 core assertion: **the only headings are the `#` title and the `### ` rules** — blocks stray `##`/`####` sections. "Prose under a rule" stays allowed (#6's comment allowance).
- **Expected:** `rules.md` is provably preamble-stub + rules; no random sections accumulate.

## 11. Drop `strict: true` (1:1) on Arch + Structure; keep `requireAllCited`

- **What:** Arch/Structure use `strict: true` (1:1 — a Rule cited by >1 test is an error). `strict` adds exactly one check over the default: the duplicate-citation ban. All other guarantees (rule enforced, test cites real rule, no undocumented rule, no bare test) come from the default + `requireAllCited`, which both types already carry.
- **Why:** 1:1 conflates "one logical invariant" with "exactly one test method." It blocks a legitimate case: a universal sweep **plus** focused edge-case / positive-characterization methods under the same Rule (e.g. "a feature MAY depend on a peer's Contracts leaf" proven as its own method, not buried in a regex). 1:1 forces cramming into one method or inventing a throwaway Rule. The anti-erosion benefit is partly illusory — 1:1 guarantees singularity, not exhaustiveness.
- **Change:** `ParityGuard.For(typeof(ParityTests)).Assert(..., requireAllCited: true)` on both (drop `strict: true`). Arch/Structure are the only callers of `strict`, so also delete the knob from `ParityGuard`: the `strict` ctor param + the `duplicated` branch + the `For(strict)` overload, and simplify the `1:{N|1}` text in the failure message to `1:N`. Update the Arch/Structure ParityTests comments and Harness README § Cardinality.
- **Expected:** Edge-case methods allowed under one invariant; `ParityGuard` collapses to one behavior (default + optional `requireAllCited`), no dead knob; only the Rulebook↔method 1:1 navigation map is given up (behavioral types already live without it).
