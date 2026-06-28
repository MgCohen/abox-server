# tests/ — the Rulebook discipline

> **Tests are co-located ([`../PLANS/test-colocation.md`](../PLANS/test-colocation.md)).** A feature's
> `Unit`/`Wire`/`E2E`/`Live` live with the feature under `src/<…>/<Owner>/Tests/` (`ABox.<Owner>.Tests`),
> glob-discovered by `dirs.proj` and policed by the harness's own tests. Under `tests/` now: the ownerless
> types (`Tests/` = `Arch`/`Structure`/`Docs` in `ABox.Tests.Central`), the shared `Harness/` engine with its
> own tests at `Harness/Tests/`, the central per-type `Rubrics/`, and feature-independent `Fixtures/`. Run
> the full suite with `dotnet test dirs.proj`;
> stand up a feature's tests with the **new-feature-tests** skill.

The front door for this repo's tests. It routes; the detail lives one level down.

Three pieces:

- **[`Harness/`](Harness/README.md)** — the shared *Rulebook* engine + vocabulary: the
  `[Rule]` attribute, `ParityGuard`, and the `TestTypes` / `RepoTree`
  the harness's own tests run on. Nothing product-specific lives here.
- **[`Tests/`](Tests/README.md)** — the central, ownerless suite (`ABox.Tests.Central`): the three
  structural types `Arch`/`Structure`/`Docs`, each its own Rulebook with the same folder shape
  (`<Type>/Rulebook.md`, the test `.cs`, `<Type>/Support/`). A feature's `Unit`/`Wire`/`E2E`/`Live` are
  **co-located** (`ABox.<Owner>.Tests`), not here.
- **[`Harness/Tests/`](Harness/Tests/README.md)** — the **harness's own tests** (`ABox.Tests.Harness.Tests`),
  beside the engine they police: they guard the *test system* — taxonomy and parity — validating every suite
  from outside, the way Arch validates `src/`. They are the enforcer, not a Rulebook type, so they carry no
  Rulebook of their own.

## The discipline in one paragraph

Every test type states its guarantees as natural-language **Rules** — the `### `
headers in `<Type>/Rulebook.md` — and each Rule is enforced by a
`[Rule("<header>")]` xUnit fact beside it. The **harness's own tests** run one parity
check over every central type and every co-located feature suite, and fail the build if a Rule
has no test, or a test cites no Rule.
A test therefore never lands alone: it lands **with the Rule it proves**. This is the same
parity mechanism that guards `src/` placement — turned on the tests themselves.

## The types

Three structural types are **central** and ownerless (`ABox.Tests.Central`): `Arch`, `Structure`, `Docs`.
Four are a feature's own, **co-located** in `ABox.<Owner>.Tests`: `Unit`, `Wire`, `E2E`, `Live`. The
**harness's own tests** (`ABox.Tests.Harness.Tests`) test the **test system** itself — they are the enforcer,
not one of the types below.

| Type | Guarantees | Drives | Gating |
|------|-----------|--------|--------|
| **Arch** | dependency invariants — *"Dependencies flow down the layer graph only"* | ArchUnitNET over loaded assemblies | always |
| **Structure** | source-placement invariants — *"Every project lives under a home folder"* | filesystem scan of `src/` + `tests/` | always |
| **Unit** | expected results + seam contracts (one type / slice + local fakes) | the type + fakes | always |
| **E2E** | flow guarantees — *"claude-ping with a scripted reply → implementer reaches Completed"* | real `Composition` via `FlowHarness`, scripted provider | always |
| **Wire** | endpoint contracts — *"GET /health → ok"* | real HTTP via `WebApplicationFactory<Program>` | always |
| **Live** | real-CLI guarantees | the **real** `claude`/`codex` CLI + subscription | opt-in (`RUN_LIVE=1`), skipped in CI |
| **Docs** | structured-document guarantees — *"Every authored doc-engine instance validates against its doctype"* | shells out to `docengine check` / `validate` | always |

The **harness's own tests** (`ABox.Tests.Harness.Tests`) sit outside this table: they enforce test-system
invariants — *parity holds for every registered type*, every test lives in a registered type — by reflecting
over the central + co-located assemblies and reading the test tree on disk. They are the enforcer, not a type.

Another structural surface — *namespace mirrors folder* — is not a test: it's the SDK
analyzer **IDE0130** (`/.editorconfig`, `severity = error`, scoped to `src/` + `tests/`).

## Adding or changing a test

Use the **`test-rulebook`** skill (`.claude/skills/test-rulebook/`) — it carries the
decision table (which type) and the add-a-Rule procedure. In short: pick the type, add a
`### ` Rule to its `Rulebook.md`, add the `[Rule("<header>")]` fact, keep namespace = folder,
build + test. Adding a Rule to an existing suite needs no new csproj; standing up a **new feature's**
suite stamps one `ABox.<Owner>.Tests.csproj` via the **new-feature-tests** skill. The harness's own tests read
Rulebooks straight from the source tree.

That's *where* a test goes. For *how the test body is written* — substitute-by-ownership, AAA, and
assert-against-arranged-state — see [`Harness/authoring.md`](Harness/authoring.md), graded by
`/judge-authoring <test file>`.

```
dotnet build ABox.slnx     # the product/IDE solution (src/** + central tests)
dotnet test  dirs.proj     # the FULL suite — central + every co-located feature assembly
```

Plan of record: [`PLANS/test-colocation.md`](../PLANS/test-colocation.md).
