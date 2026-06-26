# tests/ — the Rulebook discipline

The front door for this repo's tests. It routes; the detail lives one level down.

Three pieces:

- **[`Harness/`](Harness/README.md)** — the shared *Rulebook* engine + vocabulary: the
  `[Rule]` attribute, `ParityGuard`, and the `TestTypes` / `RepoTree`
  the Meta guards run on. Nothing product-specific lives here.
- **[`Tests/`](Tests/README.md)** — the product suite (`ABox.Tests`): six **types**, each its
  own Rulebook with the same folder shape (`<Type>/Rulebook/`, `<Type>/Tests/`, `<Type>/Support/`).
- **[`Meta/`](Meta/README.md)** — the **self-suite** (`ABox.Tests.Meta`): the same Rulebook shape,
  but its Rules guard the *test system* — taxonomy, Rulebook format, parity — validating the product
  suite from outside, the way Arch validates `src/`.

## The discipline in one paragraph

Every test type states its guarantees as natural-language **Rules** — the `### `
headers in `<Type>/Rulebook/rules.md` — and each Rule is enforced by a
`[Rule("<header>")]` xUnit fact under `<Type>/Tests/`. The **Meta** self-suite runs one parity
check over every product type (and over itself) and fails the build if a Rule has no test, or a test cites no Rule.
A test therefore never lands alone: it lands **with the Rule it proves**. This is the same
parity mechanism that guards `src/` placement — turned on the tests themselves.

## The types

Seven test the **product** (in `ABox.Tests`); **Meta** tests the **test system** itself, from its own
`ABox.Tests.Meta` assembly.

| Type | Guarantees | Drives | Gating |
|------|-----------|--------|--------|
| **Arch** | dependency invariants — *"Dependencies flow down the layer graph only"* | ArchUnitNET over loaded assemblies | always |
| **Structure** | source-placement invariants — *"Every project lives under a home folder"* | filesystem scan of `src/` + `tests/` | always |
| **Unit** | expected results + seam contracts (one type / slice + local fakes) | the type + fakes | always |
| **E2E** | flow guarantees — *"claude-ping with a scripted reply → implementer reaches Completed"* | real `Composition` via `FlowHarness`, scripted provider | always |
| **Wire** | endpoint contracts — *"GET /health → ok"* | real HTTP via `WebApplicationFactory<Program>` | always |
| **Live** | real-CLI guarantees | the **real** `claude`/`codex` CLI + subscription | opt-in (`RUN_LIVE=1`), skipped in CI |
| **Docs** | structured-document guarantees — *"Every authored doc-engine instance validates against its doctype"* | shells out to `docengine check` / `validate` | always |
| **Meta** | test-system invariants — *"Parity holds for every registered type"* | reflection over the product assembly + disk over the test tree | always |

Another structural surface — *namespace mirrors folder* — is not a test: it's the SDK
analyzer **IDE0130** (`/.editorconfig`, `severity = error`, scoped to `src/` + `tests/`).

## Adding or changing a test

Use the **`test-rulebook`** skill (`.claude/skills/test-rulebook/`) — it carries the
decision table (which type) and the add-a-Rule procedure. In short: pick the type, add a
`### ` Rule to its `rules.md`, add the `[Rule("<header>")]` fact, keep namespace = folder,
build + test. No new csproj is ever needed — `Tests.csproj` globs every `src\**\ABox.*.csproj`,
and the Meta guards read Rulebooks straight from the source tree.

That's *where* a test goes. For *how the test body is written* — substitute-by-ownership, AAA, and
assert-against-arranged-state — see [`Harness/authoring.md`](Harness/authoring.md), graded by
`/judge-authoring <test file>`.

```
dotnet build ABox.slnx
dotnet test  ABox.slnx
```

Plan of record: [`PLANS/test-structure.md`](../PLANS/test-structure.md).
