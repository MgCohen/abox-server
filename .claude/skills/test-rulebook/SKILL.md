---
name: test-rulebook
description: >-
  How to add, move, or modify a test in this repo's tests/ tree. Use when writing an
  xUnit test, deciding which of the six test types (Arch, Structure, Unit, E2E, Wire,
  Live) a test belongs in, adding or editing a Rulebook (rules.md), or when a parity /
  ArchUnitNET / Structure test fails. Keeps every test paired with a Rulebook Rule so
  the ParityGuard stays green.
---

# Adding a test = adding (or citing) a Rule

Every test *type* in `tests/Tests/` is a **Rulebook**: a `<Type>/Rulebook/` folder with
`template.md` (the type's Rule shape) and `rules.md` (a preamble + the `### ` Rules), each
Rule enforced by a `[Rule("<header>")]` xUnit fact in `<Type>/Tests/`. A per-type `ParityGuard`
fact fails the build if a Rule has no test or a test cites no Rule. So a test never lands
alone — it lands **with its Rule**.

Engine: `tests/Harness/` (`Rule.cs`, `ParityGuard.cs`). Detail docs:
[`tests/README.md`](../../../tests/README.md), [`tests/Tests/README.md`](../../../tests/Tests/README.md),
[`tests/Harness/README.md`](../../../tests/Harness/README.md). The plan is
[`PLANS/test-structure.md`](../../../PLANS/test-structure.md). Read those before
inventing structure; this skill is the *procedure*.

> **Rules are a ratchet — add liberally, change rarely.** *Adding* a Rule is the everyday, safe
> move; it only tightens guarantees. *Editing, re-wording, or removing* an existing Rule is a
> **design decision** — each encodes a hard-won invariant, and parity keeps the header/test in
> lockstep but can't tell you the guarantee got weaker. *Reshaping the template/format/shape* (the
> `### ` scan, the `template.md`/`rules.md` split, layout, the completeness knob, csproj copy) is the most dangerous: it can make
> Rules silently stop being enforced across **every** type at once, with a green build. When a change
> isn't a plain add, stop and confirm — don't quietly edit. Full contract: `tests/Harness/README.md`
> § *Stability contract*.

## 1. Pick the type (where does it go?)

| The thing you're proving | Type | Drives |
|---|---|---|
| Who-depends-on-whom; a layer/reference invariant | **Arch** | ArchUnitNET over loaded assemblies |
| Where a project/file lives on disk; a placement invariant | **Structure** | filesystem scan of `src/`+`tests/` |
| One type or a small cluster in isolation (+ seam contracts with fakes) | **Unit** | the type + local fakes |
| A whole flow end-to-end with a scripted (non-real) provider | **E2E** | real `Composition` via `FlowHarness` |
| An HTTP endpoint contract | **Wire** | `WebApplicationFactory<Program>` |
| The **real** `claude`/`codex` CLI + subscription | **Live** | real CLI, gated `[LiveFact]` / `RUN_LIVE=1` |

Rule of thumb: no real network/CLI/browser → Unit unless it spans a flow (E2E) or the
HTTP surface (Wire). Real CLI → Live (and it **must** be `[LiveFact]`, never `[Fact]`,
so CI skips it).

**Need a whole new *type* (not just a Rule)?** Rare — only when no existing type can host
the guarantee (don't fork Unit into near-twins). Follow the step-by-step in
`tests/Harness/README.md` § *Standing up a new test type*: create `<Type>/{Rulebook,Tests,Support}/`,
fill `template.md` + `rules.md` from the canonical skeleton (don't copy a sibling), register the type
in `Structure/Support/TestTypes`, add the `[ParityFact]` (`requireAllCited` if the set is complete), and
write ≥1 Rule. No csproj edit needed. Don't invent a new Rulebook *shape* — reuse the uniform one.

## 2. Add the test

1. **Write the `### ` Rule** in that type's `Rulebook/rules.md`, phrased as the
   guarantee itself and matching the type's `template.md`: Arch/Structure use an invariant
   header (no arrow) — *"Dependencies flow down the layer graph only"*; behavioral types end
   in a `→` result — *"claude-ping with a scripted reply → implementer reaches Completed"*.
   Exactly one `**Why:**` bullet; any extra note is plain prose under the Rule, never another
   bold-label bullet. Reuse an existing header if your case proves an existing Rule.
2. **Write the fact** in `<Type>/Tests/` carrying both the xUnit run attribute and a
   `[Rule("<exact header>")]` citation — they compose (`[Fact]` + `[Rule]`), the Rule is
   not derived from `FactAttribute`. Live tests use `[LiveFact]` + `[Rule("<header>")]`.
3. **Keep the namespace = folder** (`ABox.Tests.<Type>...`). IDE0130 is
   `severity = error` — a mismatch fails the build.
   - **Failure messages are fix instructions.** Active voice, name the file/type, say what to do
     ("Move X to Y", "Add a [Rule] citing Z") — not "X is wrong". One direct line, no essays.
4. Put any test-only double/harness in `<Type>/Support/`; promote to the shared
   `tests/Tests/Support/` only on a genuine **second** consumer.

## 3. Completeness (the one knob)

Parity is always **1:N** — every Rule needs ≥1 test, every `[Rule]` cites a real header, a
guarantee may have several case tests. The one knob is `Assert(requireAllCited: true)`: must
*every* test in scope cite a Rule?

- **Arch / Structure / E2E / Wire → `requireAllCited: true`.** The Rulebook is the complete
  set; a bare `[Fact]` with no `[Rule]` fails parity.
- **Unit / Live → default.** They accrue going-forward, so an uncited `[Fact]` is tolerated
  until backfilled. (`ParityGuard.For(typeof(ParityTests)).Assert()`.)

## 4. Derive, don't hardcode

Assert against values pulled from the source of truth, not literals copied into the
test. Hardcoding "returns 7" or a fixed list goes red when code legitimately changes —
that's churn, not a guarantee. Literal expectations are only for **stable, structural**
facts (a path contains the repo name; a home folder is one of the agreed set), and even
then extract from the project (csproj/registry/constant) where you can. If editing
unrelated code can turn the test red, you hardcoded something you should have derived.

## 5. Things that bite

- **No new test csproj.** `tests/Tests/ABox.Tests.csproj` globs
  `src\**\ABox.*.csproj` and `**\Rulebook\*.md` — a new feature/slice or
  Rulebook is picked up automatically. Don't add a project per type.
- **Rulebook must reach the output dir.** Both `template.md` and `rules.md` are copied via the
  `None ... CopyToOutputDirectory` glob; `ParityGuard` reads `rules.md` at runtime. A Rule in a
  stray `.md` won't be seen.
- **`rules.md` holds only Rules.** The example `### ` lives in `template.md`, not `rules.md`, so
  every `### ` in `rules.md` counts — there's no fence to skip and nothing to game. Two Structure
  guards enforce this: every Rule matches its `template.md`, and `rules.md` carries no stray sections.
- **Arch model** auto-loads every production assembly from the output dir and excludes
  `*.Tests.*`. To add a layer band, add an `IObjectProvider<IType>` + `Layer` entry (with
  its `MayDependOn`) in `Arch/Support/ArchitectureModel` — the down-only rule covers it.

## 6. Verify

```
dotnet build ABox.slnx   # warning-free; IDE0130 + parity compile-time checks
dotnet test  ABox.slnx   # parity facts + your new test green (Live stays skipped)
```

A parity failure names exactly what's out of sync (Rule with no test / test citing a
missing Rule / bare test with no Rule). Fix by aligning the header and the `[Rule("...")]`
string to match exactly.
