# Test Harness — the Rulebook convention

The shared engine every test *type* in this repo is built on. It holds **only** two things — the `[Rule]`
attribute and the `ParityGuard` engine — plus this doc. Nothing type-specific lives here; a type's models,
doubles, and harnesses stay in that type's own `Support/` until a *second* type genuinely reuses them.

## Every test type is a Rulebook

A **Rulebook** is a folder `<Type>/Rulebook/` holding **two files**:

- **`template.md`** — the type's Rule *shape*: one example Rule, header + `**Why:**` bullet. The schema, in one place.
- **`rules.md`** — a short preamble, then the type's **Rules**. A **Rule** is one `### ` header here.

What a Rule *means* varies by type —

- **Arch** Rule = a dependency invariant: *"Dependencies flow down the layer graph only"*
- **Structure** Rule = a placement invariant: *"Every project lives under an agreed home folder"*
- **Unit** Rule = an expected result: *"Reverse of empty → empty"*
- **E2E / Wire / Live** Rule = a flow / endpoint / real-CLI guarantee

— but the **file shape, location, and parity discipline are identical across every type.** Splitting the
template out of `rules.md` is deliberate: `rules.md` then holds nothing but Rules (no example `### ` to skip,
nothing to game), and the two Structure guards below can enforce both halves.

## The two pieces

- **`Rule.cs`** — `[Rule("<header>")]`, an xUnit `[Fact]` that also names the Rulebook header it enforces.
  A test can't enforce a Rule without citing it. (A guarantee realized by several cases is several
  `[Rule("<same header>")]` methods — see cardinality below.)
- **`ParityGuard.cs`** — keeps a Rulebook and its tests in lockstep. Each type drops in one parity fact:

  ```csharp
  public class ParityTests
  {
      [ParityFact] public void Rulebook_and_tests_are_in_sync() =>
          ParityGuard.For(typeof(ParityTests)).Assert();
  }
  ```

  `For(anchor)` scopes `[Rule]` discovery to the anchor type's **namespace**, so multiple Rulebooks coexist
  in one assembly without counting against each other (Arch and Structure share a project — their Rules must
  not bleed). `Assert()` derives the Rulebook path from that namespace (`ABox.Tests.<Type>.Tests` →
  `<Type>/Rulebook/rules.md`), loads its `### ` headers, and compares them to the `[Rule]`s in scope, failing
  the build on any mismatch. `[ParityFact]` is a plain `[Fact]` exempt from the cite-a-Rule completeness check
  (it *is* the check), so the parity fact never has to cite itself.

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
- **Changing the shape / template / format — most dangerous, and rarely warranted.** The `### `-heading
  scan, the `template.md` / `rules.md` split, the namespace-scoped discovery + path derivation, the
  `requireAllCited` completeness knob, the `Rulebook/` + `Tests/` + `Support/` layout, the csproj copy glob,
  and the two Structure format guards (*Every Rule matches its type's template*, *Every Rulebook holds only
  rules*) — these are the engine's load-bearing assumptions, shared by **every** type at once. Reshaping the
  template or the parsing rules can make Rules silently stop being counted (enforcement evaporates with a
  *green* build) across the whole repo. Don't refactor the format casually; a change here is an architecture
  change to the test system, with the burden of proof to match.

The summary: **add Rules liberally; change or remove them deliberately; reshape the convention almost
never.**

## Completeness (the one knob)

Parity is always **1:N** — every Rule has ≥1 cited test, every `[Rule]` cites a real Rule, no Rule is
undocumented; a Rule may be realized by several case tests. The lone knob is `Assert(requireAllCited: true)`,
the *completeness* guard — **is every test in scope cited?**:

- **Arch / Structure / E2E / Wire → `requireAllCited: true`.** The Rulebook is the complete set; a bare test
  (a `[Fact]` with no `[Rule]`) is an error.
- **Unit / Live → default.** The Rulebook accrues going-forward, so an uncited `[Fact]` is tolerated until it
  is backfilled with its Rule. Every `[Rule]` still pairs with a real header.

(There is no duplicate-citation ban: a universal sweep plus a focused edge-case method may both cite the same
Rule — that's the 1:N freedom, not drift.)

## Adoption is staged

The *model* applies to all types from day one; the *authoring* does not. Arch + Structure ship complete
Rulebooks (their Rules already exist). The behavioral types (Unit / E2E / Wire / Live) get the folder shape
now and accrue Rules **going-forward** — every new behavioral test lands with its Rule; existing tests are
backfilled opportunistically, never in one swept pass. A behavioral Rulebook starts small and grows.

## The uniform per-type layout

```
<Type>/
  Rulebook/  template.md   this type's Rule shape: one example Rule (header + **Why:**)
             rules.md      a short preamble, then the type's '### ' Rules
  Tests/                   the [Rule]-tagged facts that enforce them + one ParityFact
  Support/                 optional, type-local: models, doubles, harnesses (no over-sharing)
```

Both Rulebook files are copied to the output directory so the guards can read them at runtime — the csproj
does this with a `None Include="**\Rulebook\*.md" CopyToOutputDirectory="PreserveNewest"` (so a new type needs
no csproj edit). Namespace mirrors folder, enforced at compile time by IDE0130 (`/.editorconfig`, scoped to
`tests/`), so the type-folder taxonomy can't silently drift — and `Assert()` derives the Rulebook path from
that namespace.

## Standing up a new test *type* (a new Rulebook)

A new type is rarer and weightier than a new Rule — it's a new *kind* of guarantee, so add one only when an
existing type genuinely can't host it (don't fork Unit into near-twins). Adding a Rule to an existing type
is almost always the right move instead. When a new type really is warranted, **fill the canonical skeleton**
below — don't copy a sibling and edit (that's how two `Why:` stylings and six near-identical preambles crept
in). The skeleton is the one owner of the shape:

```markdown
<!-- <Type>/Rulebook/template.md -->
# <Type> Rulebook — Rule template

The shape every Rule in rules.md must follow. <invariant header (no →) | behavioral header ending in →>;
exactly one **Why:** bullet. Anything else is a plain prose line under the Rule, never another bold-label bullet.

### <the type's header shape — `<subject> must / must not <…>` or `<…> → <result>`>
- **Why:** <what this protects>
```

```markdown
<!-- <Type>/Rulebook/rules.md -->
# <Type> Rulebook

<one line: what a Rule means for this type>. Convention, parity discipline, and the Rule shape live in
[`../../../Harness/README.md`](../../../Harness/README.md) and `template.md`.

---

### <first Rule, in the template's header shape>
- **Why:** <…>
```

Then:

1. **Create `tests/Tests/<Type>/`** with `Rulebook/`, `Tests/`, and (if needed) `Support/`. Namespace mirrors
   folder (`ABox.Tests.<Type>…`); IDE0130 enforces it.
2. **Fill `template.md` + `rules.md`** from the skeleton above — pick the header shape (invariant or
   behavioral) and adapt the one-line "what a Rule means here." Don't invent a new shape (see the stability
   contract); it's shared structure, not per-type creativity.
3. **Register the type** in `Structure/Support/TestTypes.Registered`. The *Every folder under tests holds a
   registered test type* guard goes red the moment the folder lands unregistered — this is the deliberate gate.
4. **Add the parity fact** in `<Type>/Tests/`: a single `[ParityFact]` calling
   `ParityGuard.For(typeof(ParityTests)).Assert(requireAllCited: <bool>)`. Pass `requireAllCited: true` when
   the Rulebook is the complete set (every test cites a Rule, like Arch / Structure); omit it for a
   going-forward type that backfills (Unit / Live). The path is derived from the namespace — no string.
5. **Write at least one `### ` Rule + its `[Rule("<header>")]` fact** so the type isn't an empty shell (an
   invariant type ships complete; a behavioral type may start with one Rule and grow — see *Adoption is
   staged*).
6. **No csproj edit is needed** — `Tests.csproj` already globs `**\Rulebook\*.md` to the output and compiles
   every `.cs` under the type. Rebuild; the parity fact and the two Structure format guards prove the new
   Rulebook is wired and well-formed.

The `ParityTests` anchor's namespace keeps the new type's Rules from bleeding into another type's parity —
that's why each type carries its own parity fact.
