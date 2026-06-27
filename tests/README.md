# tests/ — the Rulebook discipline

> **Tests are co-located ([`../PLANS/test-colocation.md`](../PLANS/test-colocation.md)).** A feature's
> `Unit`/`Wire`/`E2E`/`Live` live with the feature under `src/<…>/<Owner>/Tests/` (`ABox.<Owner>.Tests`),
> glob-discovered by `dirs.proj` and policed by Meta. Under `tests/` now: the ownerless types
> (`Tests/` = `Arch`/`Structure`/`Docs` in `ABox.Tests.Central`, `Meta/`), the shared `Harness/`, the central
> per-type `Templates/`, and feature-independent `Fixtures/`. Run the full suite with `dotnet test dirs.proj`;
> stand up a feature's tests with the **new-feature-tests** skill.

The front door for this repo's tests. It routes; the detail lives one level down.

Three pieces:

- **[`Harness/`](Harness/README.md)** — the shared *Rulebook* engine + vocabulary: the
  `[Rule]` attribute, `ParityGuard`, and the `TestTypes` / `RepoTree`
  the Meta guards run on. Nothing product-specific lives here.
- **[`Tests/`](Tests/README.md)** — the central, ownerless suite (`ABox.Tests.Central`): the three
  structural types `Arch`/`Structure`/`Docs`, each its own Rulebook with the same folder shape
  (`<Type>/Rulebook/`, `<Type>/Tests/`, `<Type>/Support/`). A feature's `Unit`/`Wire`/`E2E`/`Live` are
  **co-located** (`ABox.<Owner>.Tests`), not here.
- **[`Meta/`](Meta/README.md)** — the **self-suite** (`ABox.Tests.Meta`): the same Rulebook shape,
  but its Rules guard the *test system* — taxonomy, Rulebook format, parity — validating the product
  suite from outside, the way Arch validates `src/`.

## The discipline in one paragraph

Every test type states its guarantees as natural-language **Rules** — the `### `
headers in `<Type>/Rulebook/rules.md` — and each Rule is enforced by a
`[Rule("<header>")]` xUnit fact beside it. The **Meta** self-suite runs one parity
check over every central type, every co-located feature suite, and over itself, and fails the build if a Rule
has no test, or a test cites no Rule.
A test therefore never lands alone: it lands **with the Rule it proves**. This is the same
parity mechanism that guards `src/` placement — turned on the tests themselves.

## The types

Three structural types are **central** and ownerless (`ABox.Tests.Central`): `Arch`, `Structure`, `Docs`.
Four are a feature's own, **co-located** in `ABox.<Owner>.Tests`: `Unit`, `Wire`, `E2E`, `Live`. **Meta**
tests the **test system** itself, from its own `ABox.Tests.Meta` assembly.

| Type | Guarantees | Drives | Gating |
|------|-----------|--------|--------|
| **Arch** | dependency invariants — *"Dependencies flow down the layer graph only"* | ArchUnitNET over loaded assemblies | always |
| **Structure** | source-placement invariants — *"Every project lives under a home folder"* | filesystem scan of `src/` + `tests/` | always |
| **Unit** | expected results + seam contracts (one type / slice + local fakes) | the type + fakes | always |
| **E2E** | flow guarantees — *"claude-ping with a scripted reply → implementer reaches Completed"* | real `Composition` via `FlowHarness`, scripted provider | always |
| **Wire** | endpoint contracts — *"GET /health → ok"* | real HTTP via `WebApplicationFactory<Program>` | always |
| **Live** | real-CLI guarantees | the **real** `claude`/`codex` CLI + subscription | opt-in (`RUN_LIVE=1`), skipped in CI |
| **Docs** | structured-document guarantees — *"Every authored doc-engine instance validates against its doctype"* | shells out to `docengine check` / `validate` | always |
| **Meta** | test-system invariants — *"Parity holds for every registered type"* | reflection over the central + co-located assemblies + disk over the test tree | always |

Another structural surface — *namespace mirrors folder* — is not a test: it's the SDK
analyzer **IDE0130** (`/.editorconfig`, `severity = error`, scoped to `src/` + `tests/`).

## Adding or changing a test

Use the **`test-rulebook`** skill (`.claude/skills/test-rulebook/`) — it carries the
decision table (which type) and the add-a-Rule procedure. In short: pick the type, add a
`### ` Rule to its `rules.md`, add the `[Rule("<header>")]` fact, keep namespace = folder,
build + test. Adding a Rule to an existing suite needs no new csproj; standing up a **new feature's**
suite stamps one `ABox.<Owner>.Tests.csproj` via the **new-feature-tests** skill. The Meta guards read
Rulebooks straight from the source tree.

That's *where* a test goes. For *how the test body is written* — substitute-by-ownership, AAA, and
assert-against-arranged-state — see [`Harness/authoring.md`](Harness/authoring.md), graded by
`/judge-authoring <test file>`.

```
dotnet build ABox.slnx     # the product/IDE solution (src/** + central tests)
dotnet test  dirs.proj     # the FULL suite — central + every co-located feature assembly
```

Plan of record: [`PLANS/test-colocation.md`](../PLANS/test-colocation.md).
