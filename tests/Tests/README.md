# Tests — the central, ownerless suite (`ABox.Tests.Central`)

> **Co-location ([`../../PLANS/test-colocation.md`](../../PLANS/test-colocation.md)).** This tree now holds
> only the **ownerless** types — `Arch`, `Structure`, `Docs` — in `ABox.Tests.Central`. A feature's
> `Unit`/`Wire`/`E2E`/`Live` live with the feature under `src/<…>/<Owner>/Tests/`; the `Meta` self-suite
> (`../Harness/Meta`) polices every assembly. Use **new-feature-tests** to add a feature's suite.

The central test assembly (`ABox.Tests.Central`). Every test *type* is a Rulebook with the same folder shape
(`<Type>/Rulebook/`, `<Type>/Tests/`, `<Type>/Support/`) — see [`../Harness/README.md`](../Harness/README.md)
for the convention and the parity discipline. The ownerless types coexist in one assembly because parity
scopes `[Rule]` discovery by namespace, so each type's Rulebook is counted against its own tests only.

These three structural types are ownerless and live here. The behavioral types (`Unit`/`Wire`/`E2E`/`Live`)
are a feature's own and co-locate in `ABox.<Owner>.Tests` under `src/<…>/<Owner>/Tests/`. The test-system's
own checks live apart, in the [`../Harness/Meta/`](../Harness/Meta/README.md) self-suite (`ABox.Tests.Meta`), nested
with the engine it polices, which validates every suite from outside.

- **`Arch/`** — the **reference graph** (who depends on whom), via ArchUnitNET over the *loaded* assemblies.
  `Support/ArchitectureModel` defines the layer bands + the allow-graph the down-only rule is derived from.
  It discovers production assemblies from the output dir and excludes anything named `*.Tests.*` — so this
  merged assembly excludes itself.
- **`Structure/`** — **source placement** on disk, via a filesystem scan. `Support/SourceTree` sees every
  project folder under `src/`/`tests/` (compiled or not); `Support/HomeFolders` is the agreed-folder model.
- **`Docs/`** — **structured-document** guarantees, by shelling out to the `docengine` CLI (ADR 0015 — the
  harness runs the engine, never links it).

The **test system's own** invariants (parity, taxonomy) are not here — they live in the
[`../Harness/Meta/`](../Harness/Meta/README.md) self-suite, which reflects over this assembly from outside.
(Rulebook *format* is the `Docs` type's job, via the doc-engine.)

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
| Add a behavioral rule (Unit/E2E/Wire/Live) | **co-located, not here** — append a `###` block to the feature's `src/<…>/<Owner>/Tests/<Type>/Rulebook/rules.md` + a `[Rule("<header>")]` test beside it (1:N allowed); Meta polices it. Stand up a new feature's suite with **new-feature-tests** |
| Add a test-system invariant (Meta) | rare — append a `###` block to [`../Harness/Meta/Rulebook/rules.md`](../Harness/Meta/Rulebook/rules.md) + a `[Rule("<header>")]` test in `../Harness/Meta/Tests/`; these guard the taxonomy/Rulebooks/parity, not the product |
| Add a whole new test *type* | rare — only when no existing type fits. Follow [`../Harness/README.md`](../Harness/README.md) § *Standing up a new test type*: create `<Type>/{Rulebook,Tests,Support}/`, fill `template.md` + `rules.md` from the canonical skeleton, register it in `Harness/TestTypes`, write ≥1 Rule. No csproj edit, no parity fact (the Meta self-suite runs parity once registered). |
| Add a production assembly / feature / slice | **nothing** — the csproj globs `src\**\ABox.*.csproj`, so a new `ABox.*` project is referenced and governed automatically |
| Add a layer band | add one `IObjectProvider<IType>` band + a `Layer` entry (with its `MayDependOn`) in `Arch/Support/ArchitectureModel`; the down-only rule covers it automatically |
| Evict a pending folder | drop it from `HomeFolders.PendingEviction`; the staleness check fails once the folder is gone, as the reminder to do so |
