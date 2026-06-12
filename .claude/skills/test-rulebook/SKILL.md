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

Every test *type* in `tests/Tests/` is a **Rulebook**: a `<Type>/Rulebook/rules.md`
whose `### ` headers are the guarantees, enforced by `[Rule("<header>")]` xUnit facts
in `<Type>/Tests/`. A per-type `ParityGuard` fact fails the build if a Rule has no test
or a test cites no Rule. So a test never lands alone — it lands **with its Rule**.

Engine: `tests/Harness/` (`Rule.cs`, `ParityGuard.cs`). Detail docs:
[`tests/README.md`](../../../tests/README.md), [`tests/Tests/README.md`](../../../tests/Tests/README.md),
[`tests/Harness/README.md`](../../../tests/Harness/README.md). The plan is
[`PLANS/test-structure.md`](../../../PLANS/test-structure.md). Read those before
inventing structure; this skill is the *procedure*.

> **Rules are a ratchet — add liberally, change rarely.** *Adding* a Rule is the everyday, safe
> move; it only tightens guarantees. *Editing, re-wording, or removing* an existing Rule is a
> **design decision** — each encodes a hard-won invariant, and parity keeps the header/test in
> lockstep but can't tell you the guarantee got weaker. *Reshaping the template/format/shape* (the
> `### ` scan, fenced-block skip, layout, cardinality, csproj copy) is the most dangerous: it can make
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
copy a sibling's preamble + template, add the `Parity` fact with the right strictness, and write
≥1 Rule. No csproj edit needed. Don't invent a new Rulebook *shape* — reuse the uniform one.

## 2. Add the test

1. **Write the `### ` Rule** in that type's `Rulebook/rules.md`, phrased as the
   guarantee itself (Arch/Structure: an invariant — *"Dependencies flow down the layer
   graph only"*; behavioral: a result — *"claude-ping completes with PONG"*). Match the
   preamble's template; reuse an existing header if your case proves an existing Rule.
2. **Write the fact** in `<Type>/Tests/` tagged `[Rule("<exact header>")]` (it derives
   from `FactAttribute`, so it *is* the `[Fact]`). Live tests use `[LiveFact("<header>")]`.
3. **Keep the namespace = folder** (`RemoteAgents.Tests.<Type>...`). IDE0130 is
   `severity = error` — a mismatch fails the build.
   - **Failure messages are fix instructions.** Active voice, name the file/type, say what to do
     ("Move X to Y", "Add a [Rule] citing Z") — not "X is wrong". One direct line, no essays.
4. Put any test-only double/harness in `<Type>/Support/`; promote to the shared
   `tests/Tests/Support/` only on a genuine **second** consumer.

## 3. Cardinality (the one knob)

- **Arch / Structure → strict 1:1.** One invariant, one test. A Rule tested twice, or a
  test with no Rule, fails parity. (`ParityGuard.For(typeof(Parity), strict: true)`.)
- **Unit / E2E / Wire / Live → 1:N.** One guarantee may have several case tests; every
  Rule still needs ≥1 test and every `[Rule]` must cite a real header.

## 4. Things that bite

- **No new test csproj.** `tests/Tests/RemoteAgents.Tests.csproj` globs
  `src\**\RemoteAgents.*.csproj` and `**\Rulebook\*.md` — a new feature/slice or
  Rulebook is picked up automatically. Don't add a project per type.
- **Rulebook must reach the output dir.** It's copied via the `None ... CopyToOutputDirectory`
  glob; `ParityGuard` reads it at runtime. A Rule in a stray `.md` won't be seen.
- **Fenced ``` blocks are skipped** when counting Rules, so a Rulebook's own template can
  show a sample `### ` without it counting as a declared Rule.
- **Arch model** auto-loads every production assembly from the output dir and excludes
  `*.Tests.*`. To add a layer band, add an `IObjectProvider<IType>` + `Layer` entry (with
  its `MayDependOn`) in `Arch/Support/ArchitectureModel` — the down-only rule covers it.

## 5. Verify

```
dotnet build RemoteAgents.slnx   # warning-free; IDE0130 + parity compile-time checks
dotnet test  RemoteAgents.slnx   # parity facts + your new test green (Live stays skipped)
```

A parity failure names exactly what's out of sync (Rule with no test / test citing a
missing Rule / Rule tested twice). Fix by aligning the header and the `[Rule("...")]`
string to match exactly.
