# Tests — the product suite (six Rulebook types)

The product test assembly (`ABox.Tests`). Every test *type* is a Rulebook with the same folder shape
(`<Type>/Rulebook/`, `<Type>/Tests/`, `<Type>/Support/`) — see [`../Harness/README.md`](../Harness/README.md)
for the convention and the parity discipline. The six types coexist in one assembly because parity scopes
`[Rule]` discovery by namespace, so each type's Rulebook is counted against its own tests only.

These six test the **product**. The test-system's own checks live apart, in the sibling
[`../Meta/`](../Meta/README.md) self-suite (`ABox.Tests.Meta`), which validates this suite from outside.

- **`Arch/`** — the **reference graph** (who depends on whom), via ArchUnitNET over the *loaded* assemblies.
  `Support/ArchitectureModel` defines the layer bands + the allow-graph the down-only rule is derived from.
  It discovers production assemblies from the output dir and excludes anything named `*.Tests.*` — so this
  merged assembly excludes itself.
- **`Structure/`** — **source placement** on disk, via a filesystem scan. `Support/SourceTree` sees every
  project folder under `src/`/`tests/` (compiled or not); `Support/HomeFolders` is the agreed-folder model.
- **`Unit/`** — isolated behavior of one type or a small cluster.
- **`E2E/`** — a flow driven end to end through an injectable provider (`Support/FlowHarness`).
- **`Wire/`** — HTTP smoke over `WebApplicationFactory<Program>`.
- **`Live/`** — real-CLI smoke, gated behind `[LiveFact]` / `RUN_LIVE=1`.

The **test system's own** invariants (parity, taxonomy, Rulebook format) are not here — they live in the
sibling [`../Meta/`](../Meta/README.md) self-suite, which reflects over this assembly from outside.

Another structural surface — *namespace mirrors folder* — is not a test: it's the SDK analyzer **IDE0130**,
enforced at compile time (`/.editorconfig`, scoped to `src/` and `tests/`).

## How to extend

> **Adding is safe; changing is not.** Appending a new Rule only tightens guarantees — do it freely.
> *Editing, removing, or re-wording* an existing Rule is a design decision (it can silently weaken a
> hard-won invariant), and *reshaping the template/format* of a Rulebook is dangerous enough to avoid
> almost always (it can break enforcement across every type at once). See
> [`../Harness/README.md`](../Harness/README.md) § *Stability contract* before doing anything but add.

| Want to… | Do this |
|----------|---------|
| Add an Arch rule | append a `###` block to `Arch/Rulebook/rules.md` + a `[Rule("<header>")]` test in `Arch/Tests/RuleTests.cs` |
| Add a Structure rule | append a `###` block to `Structure/Rulebook/rules.md` + a `[Rule("<header>")]` test in `Structure/Tests/StructureTests.cs` (source placement only) |
| Add a behavioral rule (Unit/E2E/Wire/Live) | append a `###` block to that type's `Rulebook/rules.md` + a `[Rule("<header>")]` test under its `Tests/` (1:N — several cases per Rule allowed) |
| Add a test-system invariant (Meta) | rare — append a `###` block to [`../Meta/Rulebook/rules.md`](../Meta/Rulebook/rules.md) + a `[Rule("<header>")]` test in `../Meta/Tests/`; these guard the taxonomy/Rulebooks/parity, not the product |
| Add a whole new test *type* | rare — only when no existing type fits. Follow [`../Harness/README.md`](../Harness/README.md) § *Standing up a new test type*: create `<Type>/{Rulebook,Tests,Support}/`, fill `template.md` + `rules.md` from the canonical skeleton, register it in `Harness/TestTypes`, write ≥1 Rule. No csproj edit, no parity fact (the Meta self-suite runs parity once registered). |
| Add a production assembly / feature / slice | **nothing** — the csproj globs `src\**\ABox.*.csproj`, so a new `ABox.*` project is referenced and governed automatically |
| Add a layer band | add one `IObjectProvider<IType>` band + a `Layer` entry (with its `MayDependOn`) in `Arch/Support/ArchitectureModel`; the down-only rule covers it automatically |
| Evict a pending folder | drop it from `HomeFolders.PendingEviction`; the staleness check fails once the folder is gone, as the reminder to do so |
