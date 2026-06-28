# Test Harness — the Rulebook convention

The shared engine and vocabulary every test *type* is built on: the `[Rule]` attribute, the `ParityGuard`
engine, the `TestMarkers`/`TestTypes` registries, and the `RepoTree` disk
locator — plus this doc. Nothing *product*-specific lives here; a type's models, doubles, and harnesses stay
in that type's own `Support/` until a *second* type genuinely reuses them.

This doc is the **framework** layer — where a test lives and that parity enforces it. The **craft** layer —
what a test *body* should look like and check (substitute-by-ownership, AAA, assert-against-arranged-state)
— lives in [`authoring.md`](authoring.md), graded by `/judge-authoring` rather than parity, because "is this
a *good* test" is a semantic judgment, not a structural one.

## Every test type is a Rulebook

A **Rulebook** is a folder `<Type>/Rulebook/` holding **two files**, each a doc-engine instance carrying a
`docType` front-matter header:

- **`<Type>.md`** (`docType: rubric`) — the type's context home: a `## Summary` paragraph and a
  `## Criteria` list of `### ` items (the per-type semantic rubric `/judge-rulebook` grades Rules against).
- **`rules.md`** (`docType: rulebook`) — front-matter (`testType` + the `rubric`/`harness` pointers), then a
  `## Rules` list of the type's **Rules**. A **Rule** is one `### ` header here.

What a Rule *means* varies by type —

- **Arch** Rule = a dependency invariant: *"Dependencies flow down the layer graph only"*
- **Structure** Rule = a source-placement invariant: *"Every project lives under an agreed home folder"*
- **Unit** Rule = an expected result: *"Reverse of empty → empty"*
- **E2E / Wire / Live** Rule = a flow / endpoint / real-CLI guarantee

The three structural types (Arch/Structure/Docs) are ownerless and live under `tests/Tests/` in the
`ABox.Tests.Central` assembly; the four behavioral types (Unit/Wire/E2E/Live) are a feature's own and
co-locate in `ABox.<Owner>.Tests` under `src/<…>/<Owner>/Tests/`. The **harness's own tests**
(`ABox.Tests.Harness.Tests`, at `tests/Harness/Tests/`) make sure every suite follows this contract — the
taxonomy and parity — nesting beside the engine they police and validating every suite from *outside* (via
`ABox.Tests.SuiteAnchor` and `Suites.Colocated()`) the way the Arch guards validate `src`. They are **not** a
Rulebook type themselves — they are the enforcer, not part of the taxonomy they enforce (see
[`Tests/README.md`](Tests/README.md)). The **Rulebook shape and parity discipline are identical across every
product type**. Splitting the rubric out of `rules.md` is deliberate: `rules.md` then holds nothing but its
front-matter and Rules (no example `### ` to skip, nothing to game), while `<Type>.md` owns all context — the
summary and the judge criteria. The **Docs** type validates both files' shape against the doc-engine; the
harness's own tests own parity (Rule ↔ test).

## The pieces

- **`Rule.cs`** — `[Rule("<header>")]`, sits on an xUnit `[Fact]` and names the Rulebook header it enforces.
  A test can't enforce a Rule without citing it. (A guarantee realized by several cases is several
  `[Rule("<same header>")]` methods — see *Completeness* below.)
- **`ParityGuard.cs`** — keeps one type's Rulebook and its `[Rule]` tests in lockstep, scoped to a single
  namespace so types sharing an assembly don't bleed into each other's parity: `ABox.Tests.<Type>.Tests` for a
  central type (`For`), `ABox.<Owner>.Tests.<Type>` for a co-located feature type (`ForColocated`, which finds
  the Rulebook in the source tree via the assembly's `TestsSourceDir` metadata).
- **`TestTypes` / `TestMarkers` / `RepoTree`** — the test-system vocabulary the **harness's own tests**
  run on: the registry of types + the completeness flag, the run-attribute names, and
  the on-disk locator. Rulebook *format* is now validated by the doc-engine (shelled out by the **Docs** type),
  not a Harness parser.

Parity is driven **once**, from the harness's own tests — over every central type and every co-located feature
suite (`Suites.Colocated()` → `ParityGuard.ForColocated`) — so there is no per-type or per-feature parity fact:

  ```csharp
  // Tests/ParityTests.cs
  var product = typeof(SuiteAnchor).Assembly;
  foreach (var type in TestTypes.Registered)
      ParityGuard.For(product, type).Assert();
  ```

`ParityGuard.For` maps a product type to its namespace and Rulebook path through `TestTypes`
(`<Type>` → `ABox.Tests.<Type>.Tests` + `tests/Tests/<Type>/Rulebook/rules.md`), reads the Rulebook's `### `
headers **from the source tree** (`RepoTree`, not the output dir), and compares them to the `[Rule]`s in that
namespace — failing the build on any mismatch. The harness's own tests carry no Rulebook and cite no `[Rule]` —
they are the enforcer, outside the taxonomy they enforce.

## Failure output: active voice, say how to fix

A Rule's assertion message is read the moment something breaks. Write it as a **fix instruction, not a
description** — active voice, name the file/type/symbol, say what to do. No essays; one direct line.
`ParityGuard`'s own message is the model: *"Fix: align each '### <name>' header with a [Rule("<name>")]
test so the names match exactly."* Prefer that shape over "the rulebook and tests do not match".

## Derive expectations; don't hardcode what drifts

Assert against a value pulled from the source of truth, not a literal copied into the test. Hardcoding
"returns 7" or a fixed list pins an accident — it goes red the moment the code legitimately changes, testing
churn instead of a guarantee. Reserve literal expectations for **stable, structural** facts (a path contains
the repo name; a home folder is one of the agreed set), and even then **extract from the project** where you
can — read the csproj/registry/constant rather than restating its string. Rule of thumb: if editing unrelated
code can turn the test red, you hardcoded something you should have derived. (The Arch rules model this — the
down-only rule is *derived* from one allow-graph, and the csproj *globs* assemblies instead of listing them.)

The behavioral companion to this — *assert against arranged state*, and the **tautological assertion** it
guards against — lives in [`authoring.md`](authoring.md) § 4.

## Stability contract — a Rulebook is a ratchet

Treat Rules as a one-way ratchet, and treat *this convention itself* as load-bearing. The two are
different risk levels:

- **Adding a Rule — safe, encouraged.** A new `### ` header + its `[Rule]` test only *tightens* the
  guarantees. This is the everyday move; do it freely whenever a new invariant or guarantee is worth
  pinning.
- **Editing, removing, or re-wording an existing Rule — dangerous.** Each Rule encodes a hard-won
  invariant some past failure paid for. Removing one silently drops a guarantee the codebase still
  relies on; re-wording one can quietly narrow it. Parity will force you to keep the test's `[Rule("…")]`
  string in lockstep with the header, but it **cannot** tell you the *guarantee* got weaker. So a change
  here is a **design decision**, not a cleanup: justify why the invariant no longer holds (or moved),
  the same bar as changing the thing the Rule protects. When in doubt, ask — don't quietly edit.
- **Changing the shape / rubric / format — most dangerous, and rarely warranted.** The `### `-heading
  scan, the `<Type>.md` / `rules.md` split, the namespace-scoped discovery + path derivation, the
  universal completeness check (every marked test cites a Rule), the `Rulebook/` + `Tests/` + `Support/` layout, the csproj copy glob,
  the doc-engine doctypes (`rulebook` / `rubric`) that pin each file's shape, and the harness's own
  parity-over-every-registered-type — these are the engine's
  load-bearing assumptions, shared by **every**
  type at once. Reshaping the
  rubric or the parsing rules can make Rules silently stop being counted (enforcement evaporates with a
  *green* build) across the whole repo. Don't refactor the format casually; a change here is an architecture
  change to the test system, with the burden of proof to match.

The summary: **add Rules liberally; change or remove them deliberately; reshape the convention almost
never.**

## Completeness

Parity is always **1:N** — every Rule has ≥1 cited test, every `[Rule]` cites a real Rule, no Rule is
undocumented; a Rule may be realized by several case tests. Completeness is **universal and mandatory**: for
every type, a bare `[Fact]`/`[Theory]` with no `[Rule]` is an error — there is no opt-out. (Arch / Structure /
E2E / Wire / Unit / Live.) The harness's own tests are the one exception by design: they are the enforcer, not
a product type, so they carry no Rulebook and cite no Rule.

(There is no duplicate-citation ban: a universal sweep plus a focused edge-case method may both cite the same
Rule — that's the 1:N freedom, not drift.)

## Adoption is complete

Every registered type is fully backfilled and enforced: each Rulebook is the complete set for its type, and
parity requires every test to cite a Rule with no going-forward exemption. A new test of any type now lands
with its Rule or the build fails — the ratchet is closed.

> **Mid-migration (PLANS/test-colocation.md):** the per-type `<Type>.md` files now live centrally in
> `tests/Rubrics/<Type>.md`, and `rules.md` `rubric:` front-matter points there. The
> `<Type>/Rulebook/<Type>.md` shown below is the pre-move layout; this walkthrough is rewritten in full at
> Phase 5 once test homes settle. Today's truth: rubrics are central, rulebooks are per-type (soon
> per-feature), and the `rubric:` link is a relative path into `tests/Rubrics/`.

## The uniform per-type layout

```
<Type>/
  Rulebook/  rules.md      the Rubric:/Harness: pointer links, then the type's '### ' Rules
  Tests/                   the [Rule]-tagged facts that enforce them
  Support/                 optional, type-local: models, doubles, harnesses (no over-sharing)
tests/Rubrics/<Type>.md   the per-type criteria (one home for all types)
```

There is no per-type parity fact — the harness's own tests run parity over every type at once. They
read both Rulebook files straight from the **source tree** (`RepoTree` walks up to the `ABox.slnx` marker), so
no csproj copy step is needed and a new type wires in with zero csproj edits. Namespace mirrors folder,
enforced at compile time by IDE0130 (`/.editorconfig`, scoped to `tests/`), so the type-folder taxonomy can't
silently drift — and parity derives each Rulebook path from that namespace.

## Standing up a new test *type* (a new Rulebook)

A new type is rarer and weightier than a new Rule — it's a new *kind* of guarantee, so add one only when an
existing type genuinely can't host it (don't fork Unit into near-twins). Adding a Rule to an existing type
is almost always the right move instead.

> **Two homes.** A new **central, ownerless** structural type (like Arch/Structure/Docs — guarantee owned by
> the repo) follows the steps below under `tests/Tests/<Type>/`, registered in `TestTypes.Registered` and
> classified `Central`. A new **behavioral** type owned by a feature (Unit/Wire/E2E/Live-shaped) co-locates in
> the feature's `ABox.<Owner>.Tests` under `src/<…>/<Owner>/Tests/<Type>/`, with namespace
> `ABox.<Owner>.Tests.<Type>` and a csproj that stamps `TestsSourceDir` — use the **new-feature-tests** skill,
> which `ParityGuard.ForColocated` + the harness's coverage/taxonomy sweeps then police automatically. The skeleton
> below is shared by both; only the home, namespace, and (co-located) the csproj differ.

When a new type really is warranted, **fill the canonical skeleton** below — don't copy a sibling and edit
(that's how two `Why:` stylings and six near-identical preambles crept in). The skeleton is the one owner of
the shape:

```markdown
<!-- tests/Rubrics/<Type>.md  (central — one home for every type's criteria) -->
---
docType: rubric
testType: <type>
---

## Summary
<one paragraph: what a Rule means for this type, how it's proven, and where it's enforced.>

## Criteria

### <id>
<one semantic check the judge grades a Rule against — judgment only, not mechanical shape>

### <id>
<…>
```

```markdown
<!-- <Type>/Rulebook/rules.md -->
---
docType: rulebook
testType: <type>
rubric: ../../../Rubrics/<Type>.md
harness: ../../../Harness/README.md
---

## Rules

### <first Rule — `<subject> must / must not <…>` (invariant) or `<…> → <result>` (behavioural)>
- **Why:** <…>
```

Then:

1. **Create `tests/Tests/<Type>/`** with `Rulebook/`, `Tests/`, and (if needed) `Support/`. Namespace mirrors
   folder — `ABox.Tests.<Type>.Tests` for files in `<Type>/Tests/`; IDE0130 is `severity = error`, so a
   mismatch is a build error, not a warning.
2. **Fill `<Type>.md` + `rules.md`** from the skeleton above — pick the header shape (invariant or
   behavioural), adapt the summary, and write the first Rule. **`<Type>.md` must carry a `## Criteria`
   block** — at least one `### <id>` item of semantic judgment for the judge (as many as the type
   needs); the doc-engine's `rubric` doctype requires it, so the **Docs** validation fails the build if
   it's missing. Don't invent a new shape (see the stability contract); it's shared structure, not per-type creativity.
3. **Register the type** in `Harness/TestTypes.Registered`. The *every folder under tests holds a registered
   test type* guard (in the harness's own tests) goes red the moment the folder lands unregistered — this is the
   deliberate gate.
4. **Write a `### ` Rule + its `[Rule("<header>")]` fact for every test** in `<Type>/Tests/`. Completeness is
   mandatory: the type ships fully cited from the start (there is no going-forward exemption).
5. **No csproj edit and no parity fact are needed** — `Tests.csproj` compiles every `.cs` under the type, the
   harness's own tests read your Rulebook straight from the source tree and run parity over your new type the
   moment it's registered. Rebuild; the harness's parity + the Docs format guards prove it's wired and well-formed.

Parity scopes by the `ABox.Tests.<Type>.Tests` namespace, so the new type's Rules never bleed into another
type's parity — registering the type is all the wiring there is.
