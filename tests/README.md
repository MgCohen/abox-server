# tests/ — the Rulebook discipline

The front door for this repo's tests. It routes; the detail lives one level down.

Two pieces:

- **[`Harness/`](Harness/README.md)** — the shared *Rulebook* engine: the `[Rule]`
  attribute and the `ParityGuard`. Nothing type-specific lives here.
- **[`Tests/`](Tests/README.md)** — the single test assembly, organized into six
  **types**, each its own Rulebook with the same folder shape
  (`<Type>/Rulebook/`, `<Type>/Tests/`, `<Type>/Support/`).

## The discipline in one paragraph

Every test type states its guarantees as natural-language **Rules** — the `### `
headers in `<Type>/Rulebook/rules.md` — and each Rule is enforced by a
`[Rule("<header>")]` xUnit fact under `<Type>/Tests/`. A per-type `ParityGuard` fact
fails the build if a Rule has no test, or a test cites no Rule. A test therefore never
lands alone: it lands **with the Rule it proves**. This is the same parity mechanism
that guards `src/` placement — turned on the tests themselves.

## The six types

| Type | Guarantees | Drives | Gating |
|------|-----------|--------|--------|
| **Arch** | dependency invariants — *"Dependencies flow down the layer graph only"* | ArchUnitNET over loaded assemblies | always |
| **Structure** | placement invariants — *"Every project lives under a home folder"* | filesystem scan of `src/` + `tests/` | always |
| **Unit** | expected results + seam contracts (one type / slice + local fakes) | the type + fakes | always |
| **E2E** | flow guarantees — *"claude-ping completes with PONG"* | real `Composition` via `FlowHarness`, scripted provider | always |
| **Wire** | endpoint contracts — *"`/health` returns ok"* | real HTTP via `WebApplicationFactory<Program>` | always |
| **Live** | real-CLI guarantees | the **real** `claude`/`codex` CLI + subscription | opt-in (`RUN_LIVE=1`), skipped in CI |

A seventh structural surface — *namespace mirrors folder* — is not a test: it's the SDK
analyzer **IDE0130** (`/.editorconfig`, `severity = error`, scoped to `src/` + `tests/`).

## Adding or changing a test

Use the **`test-rulebook`** skill (`.claude/skills/test-rulebook/`) — it carries the
decision table (which type) and the add-a-Rule procedure. In short: pick the type, add a
`### ` Rule to its `rules.md`, add the `[Rule("<header>")]` fact, keep namespace = folder,
build + test. No new csproj is ever needed — `Tests.csproj` globs every `src\**\ABox.*.csproj`
and every `**\Rulebook\*.md`.

```
dotnet build ABox.slnx
dotnet test  ABox.slnx
```

Plan of record: [`PLANS/test-structure.md`](../PLANS/test-structure.md).
