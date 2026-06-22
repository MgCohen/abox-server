# Test authoring — doubles, shape, expectations

**Status:** ✅ applied 2026-06-15 — **superseded as the controller** by
[`tests/Harness/authoring.md`](../../tests/Harness/authoring.md) (the distilled craft convention + its
`## Criteria`, graded by `/judge-authoring`). This file remains the **decision record**: the *why*,
the alternatives weighed, and the sources. Read it for rationale; author tests against `authoring.md`.

Captures the *how-a-test-is-written* rules we settled while integrating two in-flight branches — one
moving the HTTP layer to FastEndpoints, one adding a JSON-file repository — whose shared `/projects`
endpoint forced the question. This is a **decisions + rollout** doc, not a cleanup plan — see *Non-goals*.

## TL;DR

When you write a test in this repo, four rules apply:

1. **Substitute by ownership.** Use a real instance or a hand-rolled fake for dependencies *we
   own* (our repository/store). Mock only *unmanaged* dependencies — things observable outside our
   system (the `claude`/`codex` CLI, SMTP, third-party HTTP) — and only behind our own interface.
   Verify the **result** (state), never which calls happened (interactions).
2. **One swap mechanism.** Register your double in `WebApplicationFactory`'s `ConfigureTestServices`;
   last registration wins, so it shadows the real one. That's *how* to swap — separate from
   *whether/what* to swap (rule 1).
3. **AAA body.** Arrange → Act → Assert, as three blank-line-separated blocks with one Act call.
   No `// Arrange` comments (house no-comments rule).
4. **No magic values; assert against arranged state.** Every expected value must trace to
   something the test *arranged* (a constructed object's known state, an injected fake's data, or —
   for structural tests — the project itself). Never assert against the same live source the code
   under test reads — that's a *tautological assertion* and it can never fail for the reason that
   matters.

**Scope:** the authoring contract [`test-structure.md`](test-structure.md) deferred. That plan
fixed the *taxonomy* (six types, every type a Rulebook, parity-guarded) and chose **staged adoption**
— *"behavioral Rules accrue going-forward, not backfilled."* (That staging is now **complete**: every
type is fully cited and the build rejects an uncited test — see
[`tests/Harness/README.md`](../../tests/Harness/README.md) § *Adoption is complete*.) It never said
what "well-authored" means for a behavioral test. This doc does: **what to substitute, how the
substitution works, the shape of the body, and where expected values come from.**

**What controls the test discipline (and what this doc is):** the living controllers are
[`tests/Harness/README.md`](../../tests/Harness/README.md) — the Rulebook *convention* — plus
[`tests/Tests/README.md`](../../tests/Tests/README.md) (the six-type map + how-to-extend), the
`test-rulebook` skill (the procedure), and `ParityGuard` (the teeth). [`test-structure.md`](test-structure.md)
is the *plan* that built that structure: frozen history ("✅ built"), **not** a controller. This doc
records the authoring decisions; their canonical home is the **convention doc + the skill** (see
*Rollout*) — it controls nothing until folded there.

---

## Why this came up

The `/projects` endpoint reads a list from `IRepository<Project>` and returns it as DTOs. The
first Wire test asserted the response against **the same repository the endpoint reads** — it
only proved "the endpoint echoes its own store," which can never fail for the reason that
matters. That is a named anti-pattern (a **tautological assertion**), and it exposed that the
repo's expectations guidance — mirrored in **both** the `test-rulebook` skill (§4) **and** the
controlling convention doc [`tests/Harness/README.md`](../../tests/Harness/README.md) (§ *Derive
expectations; don't hardcode what drifts*) — was steering us *into* it. Fixing it surfaced four
decisions below.

---

## Stand-ins — three kinds (don't blur them)

"Real," "provisional," and "fake" are three different axes. Conflating them pollutes the codebase:
provisional production code is meant to be *upgraded* into the real thing, while a test double
lives in `tests/` forever. Three categories:

1. **Provisional production implementation** — *real* code that runs at runtime, standing in for
   something not built yet. `JsonRepository<T>`, `ProjectSeeder`, the `StorageRoot` default (a JSON
   file because there's no database yet), `DelayStep`/the stub flow. Lives in `src/`, ships, gets
   upgraded later (JSON → real DB). House rule: label it provisional. **From a test's point of view
   it is "the real thing" — its provisional-ness is irrelevant to testing.**
2. **Test double** — test-only code that overrides behavior so a test can control input and assert.
   `FakeProjects`, `StubFlow`, `ScriptedProvider`, the proposed `FakeProjectRepository`. Lives in
   `tests/.../Support/`, never ships, stays as test infra.
3. **Test-environment configuration of a real service** — not a double; the *real* service pointed
   somewhere safe. `WireApp` swapping `StorageRoot` to a temp dir is still the real
   `JsonRepository`, just isolated from `~/.abox`.

Two consequences that bit the worked example below:

- **The fake/real axis (decision 1) is orthogonal to provisional/final.** When decision 1 says "use
  a real instance or a fake," the *real instance* is the current production implementation **even
  though it's provisional** (`JsonRepository`). Provisional ≠ double.
- **`ProjectSeeder` is category 1** (real startup behavior) and **`StorageRoot` is category 3 and
  global** (it isolates *every* `JsonRepository<T>`). A test that controls the project list must
  **turn the seeder off in the test host** and **keep the temp `StorageRoot`** — dropping
  `StorageRoot` would leak other entities' writes to `~/.abox`.

---

## Decisions

### 1. Test doubles — what to substitute, and with what

The deciding axis is **who controls the dependency**, from Khorikov's *When to Mock*:

- **Managed dependency** — out-of-process but fully ours, only reachable through our app (our
  repository / the JSON store). Communication with it is an *implementation detail*.
- **Unmanaged dependency** — observable outside our system (the `claude`/`codex` CLI process,
  an SMTP server, a third-party HTTP API). Communication with it is *observable behavior*.

The rule:

> **Use a real instance or a hand-rolled fake for managed dependencies we own. Mock only
> unmanaged dependencies, and only behind an interface we own. Verify state (the observable
> result), never interactions (which calls happened).**

Consequences for this repo:

- The repository is managed → in a test it's a **real instance (the provisional `JsonRepository`,
  category 1) or an in-memory fake (category 2)**, never a call-verifying mock. Mocking it would
  couple the test to *how* the endpoint queries — fragile, and it re-creates the tautology.
  (Microsoft's EF Core *Choosing a testing strategy* also sanctions the **repository abstraction
  as the data-layer double** — though it leans toward a real database overall, a nuance worth
  keeping. We have no ORM, just a JSON file, so both the real provisional `JsonRepository` and a
  hand-rolled fake are clean; the real `JsonRepository` keeps its own Unit coverage in
  `JsonRepositoryTests`.)
- Reserve mocks for the **unmanaged** seams — the CLI provider is already faked this way
  (`ScriptedProvider`), which is correct: it stands in for an external process.
- **Hand-rolled fakes over a mocking framework** for owned interfaces with behavior. This is the
  ecosystem default (Tyrrrz, Dunn, Dawson) *and* what this repo already does — `FakeProjects`,
  `StubFlow`, `ScriptedProvider` are all hand-rolled. No NSubstitute/Moq dependency is added.
  (If a framework is ever warranted for a one-off interaction check, NSubstitute is the
  controversy-free default; Moq's SponsorLink history rules it out.)

### 2. How substitution actually works (the mechanism)

There is **one** mechanism, uniform for every service — worth writing down because it looks like
magic:

- `IServiceCollection` is an **ordered list of `ServiceDescriptor`s**. Registering **appends**;
  it never removes.
- Resolving a **single** service (`GetRequiredService<T>()` or a constructor parameter `T`)
  returns the **last** descriptor for that type — *last-registration-wins*. (`IEnumerable<T>`
  returns all of them, in order.) The real registration isn't deleted, it's **shadowed**.
- `WebApplicationFactory`'s `ConfigureTestServices` is hooked to run **after** the app's
  `Program` registrations, so anything registered there is guaranteed last → it wins. (The
  ordinary `ConfigureServices` runs *before* the app and would lose — that's the trap.)
- Open-vs-closed generic: production registers the **open** generic `IRepository<>` →
  `JsonRepository<>`; a test registers the **closed** `IRepository<Project>`. The container
  prefers an **exact closed match** over constructing from the open generic, so the fake wins
  deterministically; other entity types still resolve to the real `JsonRepository`. This is
  **precedence, not ordering** — the closed match wins regardless of registration order, a
  *different* rule from last-wins above (which two were easy to conflate).

This is why "register your double in `ConfigureTestServices`" is the whole story for *how* to
swap. It is **not** the whole story for *whether/what* to swap — that's decision 1. Keeping the
two separate is the point: a trivial mechanism, governed by a small policy.

### 3. Test body shape — Arrange / Act / Assert

Every test body follows **Arrange → Act → Assert** (Microsoft *Unit testing best practices*;
the BDD sibling is Given/When/Then):

- **Arrange** — construct inputs and the SUT, register/seed the fakes. *This is where the test
  establishes its oracle* (see §4).
- **Act** — the one call under test.
- **Assert** — verify the observable result.

House-style wrinkle: our no-comments rule means we **do not** write `// Arrange` / `// Act` /
`// Assert`. AAA is expressed **structurally** — three blank-line-separated blocks, one Act line.
The current `WireTests` already reads this way.

### 4. Expectations — no magic values, assert against arranged state

This **reframes** the repo's current expectations guidance — which lives in *two* mirrored places,
the skill's §4 and `tests/Harness/README.md` § *Derive expectations; don't hardcode what drifts* —
whose "source of truth" wording was the actual cause of the tautology. It **augments, not discards**:
the structural-facts guidance there stays correct; we add the behavioral branch and name the failure
mode. The principle is one rule, not a structural-vs-behavioral split:

> **No magic values. Every expected value must trace to state the test *arranged* — a
> constructed object's known state, an injected fake's data, or (for structural facts) the
> project itself (csproj/registry/constant). Never derive the expectation from the same live
> source the code under test reads — that's a *tautological assertion* (both sides move
> together, the test can't fail).**

- "Magic values" (not "magic numbers" — strings, ids, and lists count too) are literals that came
  from **nowhere**. A literal is *fine* when the test established it: `enemy.Health = 10` →
  expect `5` after 5 damage. The `5` isn't magic; it's derived from arranged state. The enemy is
  the oracle, and the test owns it.
- The fake's data is likewise the oracle for a flow test: seed `[Alpha, Beta]`, assert the
  endpoint returns `[Alpha, Beta]`. That literal only changes when the *test* changes — never
  from unrelated production code — so it's not churn.
- Structural facts (Arch/Structure) still derive from the **project** (a path contains the repo
  name; a home folder is in the agreed set) — because *that* is the state those tests arrange
  against. §4's old guidance was right for these and wrong for behavioral tests; the reframe
  covers both with one sentence: **assert against what you arranged.**
- Ties to AAA: **Arrange establishes the oracle; Assert derives from Arrange.**

---

## Worked example — the `/projects` Wire test

The one test we change now (the rest of the suite is untouched — see *Non-goals*). It uses
**Option A** — a category-2 test double — chosen over B because `WireApp` is a per-class fixture, so
a real seeded store (B) bleeds across sibling tests while a fixed fake list does not. **`ProjectSeeder`
(category 1) is removed from the test host** so it can't run its real startup seed.

**Before — tautological** (reads the same repository the endpoint reads):

```csharp
var stored = await app.Services.GetRequiredService<IRepository<Project>>().GetAll();
Assert.NotEmpty(stored);

using var res = await app.CreateClient().GetAsync("/projects");

var projects = await res.Content.ReadFromJsonAsync<ProjectDto[]>();
Assert.Equal(
    stored.Select(p => (p.Id, p.Name)).OrderBy(p => p.Name),       // ← oracle = the SUT's own source
    projects!.Select(p => (p.Id, p.Name)).OrderBy(p => p.Name));
```

**After — fake at the seam, oracle owned by the test** (decisions 1–4 applied):

```csharp
// WireApp.cs — hand-rolled fake (decision 1), injected last-wins (decision 2)
internal sealed class FakeProjectRepository(IReadOnlyList<Project> projects) : IRepository<Project>
{
    public Task<IReadOnlyList<Project>> GetAll(CancellationToken ct = default) => Task.FromResult(projects);
    public Task<Project?> GetById(Guid id, CancellationToken ct = default)
        => Task.FromResult(projects.FirstOrDefault(p => p.Id == id));
    public Task Add(Project e, CancellationToken ct = default)    => throw new NotSupportedException("read-only wire fake");
    public Task Update(Project e, CancellationToken ct = default) => throw new NotSupportedException("read-only wire fake");
    public Task Remove(Guid id, CancellationToken ct = default)   => throw new NotSupportedException("read-only wire fake");
}

public IReadOnlyList<Project> KnownProjects { get; } =
[
    new(Guid.NewGuid(), "Alpha"),
    new(Guid.NewGuid(), "Beta"),
];
// in ConfigureTestServices:
services.AddSingleton<IRepository<Project>>(new FakeProjectRepository(KnownProjects));

// ProjectSeeder is category 1 (real startup behavior) — remove it from the test host so it
// neither races a seed nor calls the fake's throwing Add. (Exact removal lands in rollout.)
// StorageRoot (category 3, global) stays put — it isolates every JsonRepository<T> from ~/.abox.
```

```csharp
// WireTests.cs — AAA blocks (decision 3), assert against arranged state (decision 4)
[Rule("projects lists the projects from the store as wire DTOs")]
[Fact]
public async Task Projects_lists_the_projects_from_the_store()
{
    using var res = await app.CreateClient().GetAsync("/projects");

    Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    var projects = await res.Content.ReadFromJsonAsync<ProjectDto[]>();
    Assert.NotNull(projects);
    Assert.Equal(
        app.KnownProjects.Select(p => (p.Id, p.Name)).OrderBy(p => p.Name),   // ← oracle the test owns
        projects!.Select(p => (p.Id, p.Name)).OrderBy(p => p.Name));
}
```

---

## Rollout — what changes where

Docs + skill define the rule; one test demonstrates it. No new skill, no mass refactor.

**`.claude/skills/test-rulebook/SKILL.md`** (the procedure; it's already *the* test-authoring
skill — extend it, don't fork it)

- **Reframe §4** → "No magic values; assert against arranged state." New terminology: *arranged
  state* (what you derive from) and *tautological assertion* (the failure mode). Drop "source of
  truth." **In lockstep with** the convention doc's mirror section (below) — both change together,
  or they drift.
- **Add to §2** a test-double step: fake/real for managed deps we own; mock only unmanaged behind
  our own interface; **state over interaction**; inject via `ConfigureTestServices`
  (last-registration-wins).
- **Add** the AAA body-shape line: Arrange/Act/Assert as blank-line blocks, one Act, **no** AAA
  comments (house rule).

**`tests/Harness/README.md`** (the **convention** — the canonical controller; policy prose lives here)

- **Reframe its existing § *Derive expectations; don't hardcode what drifts*** to decision 4 — the
  mirror of the skill §4 reframe, kept identical. **Preserve** its structural-facts guidance and the
  Arch example; **add** the behavioral branch + the *tautological assertion* name.
- Add a **"Test doubles"** section (the three-kinds-of-stand-in vocabulary + decision 1 + the
  mechanism, decision 2) and a **"Test shape (AAA)"** section (decision 3). Carry the citations.

**`tests/README.md`** / **`tests/Tests/README.md`** (front door + six-type map)

- A one-line pointer to the new convention sections; **no policy prose duplicated here** — the front
  door routes, it doesn't define.

**Code (the worked example only)**

- `tests/Tests/Wire/Support/WireApp.cs` — add `FakeProjectRepository` + `KnownProjects`, register
  in `ConfigureTestServices`, **remove `ProjectSeeder`** from the test host (category 1 — else it
  races the seed / hits the fake's `Add`). **Keep** the temp `StorageRoot` seam — it's global
  category-3 isolation for every `JsonRepository<T>`, not Projects-specific.
- `tests/Tests/Wire/Tests/WireTests.cs` — assert against `app.KnownProjects`; reword `[Rule]`.
- Wire `Rulebook/rules.md` — reword the header to match.

---

## Non-goals (decided)

- **No mass test refactor now.** Structure first. The Wire test is the single worked example; the
  rest of the suite is brought into line **going-forward**, the same staged-adoption call
  `test-structure.md` already made — not in one sweep here.
- **No new skill.** `test-rulebook` is the test-authoring skill; a second "write-a-test" skill
  would collide on the same trigger and split one job. Docs point to it; it sits on the
  Rulebook/Rule/ParityGuard machinery.
- **No ADR.** The convention-doc section + skill are enough. Promote to an ADR only if this needs
  to outlive the doc.
- **No mocking-framework dependency.** Hand-rolled fakes, matching `FakeProjects`/`StubFlow`/
  `ScriptedProvider`.

---

## Review outcome

A sub-agent audit (2026-06-13) validated decisions 1–4 and the DI claims (closed-beats-open
confirmed against `Composition.cs`). Resolved:

- **A vs B → A (test double).** Both give the test *arranged state*; A was chosen because `WireApp`
  is an `IClassFixture` (one host per class), so B's real seeded store bleeds across sibling tests
  while A's fixed fake list stays isolated. (B remains the right call only if a future wire test
  needs real end-to-end serialization *and* takes on per-test store isolation.)
- **Polish at apply time:** make the "before" snippet the *verbatim* current test; show the full
  `ConfigureWebHost`/test method (cold-read); keep the EF citation nuanced (done in §1).

---

## Decisions taken

- **Three kinds of stand-in** — provisional production impl (category 1) · test double
  (category 2) · test-env config of a real service (category 3) — kept distinct. The fake/real
  axis is **orthogonal** to provisional/final.
- **Substitute by ownership** — real/fake for managed deps we own; mock only unmanaged, behind
  our own interface; verify state, not interactions.
- **Hand-rolled fakes** over a mocking framework for owned behavioral interfaces.
- **One substitution mechanism** — append + last-registration-wins via `ConfigureTestServices`;
  closed generic beats open generic. *How* to swap ≠ *whether/what* to swap.
- **AAA body shape** — Arrange/Act/Assert as blank-line blocks, one Act, no AAA comments.
- **No magic values; assert against arranged state** — reframes "derive from source of truth";
  names the *tautological assertion* failure mode.
- **Decision 4 has two canonical homes** — skill §4 and `tests/Harness/README.md` § *Derive
  expectations* — kept in lockstep. The policy's home is the **convention doc**, not the front-door
  README.
- **Wire `/projects` test uses Option A** (a `FakeProjectRepository` double) — over the real-store
  B, because the per-class fixture makes B's writes bleed across sibling tests.
- **Worked example now, suite going-forward** — no up-front backfill.
- **No new skill, no ADR, no mocking-framework dep.**

---

## Sources

- Khorikov, *When to Mock* (managed vs unmanaged) — https://enterprisecraftsmanship.com/posts/when-to-mock/
- Khorikov, *Don't mock your database, it's an implementation detail* — https://vkhorikov.medium.com/dont-mock-your-database-it-s-an-implementation-detail-8f1b527c78be
- Microsoft, *Choosing a testing strategy* (repository as the sanctioned data-layer double) — https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy
- Microsoft, *Integration tests in ASP.NET Core* (`ConfigureTestServices`, service replacement) — https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests
- Microsoft, *Unit testing best practices* (AAA; test-double terminology) — https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices
- Fowler, *Mocks Aren't Stubs* (classicist vs mockist; the double taxonomy) — https://martinfowler.com/articles/mocksArentStubs.html
- Tyrrrz, *Fakes over mocks* — https://tyrrrz.me/blog/fakes-over-mocks
- Dunn, *Prefer test doubles over mocking frameworks* (in-memory fake repository) — https://dunnhq.com/posts/2024/prefer-test-doubles-over-mocking/
- Coulman, *Tautological tests* ("never calculate the expected value") — https://randycoulman.com/blog/2016/12/20/tautological-tests/
