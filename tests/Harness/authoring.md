# Test authoring — how a good test body is written

The Rulebook convention (this folder's [README](README.md)) governs the **framework** layer — where a
test lives, that it cites a Rule, that parity holds. This doc governs the **craft** layer — what the
test *body* looks like and what it actually checks. The two are orthogonal: a test can sit in the right
type, cite its Rule, compile green, and still be a bad test — it asserts nothing that can fail, or it
couples to an implementation detail.

This layer is **not** parity-enforceable — "is this a *good* test" is a semantic judgment, not a
structural one. It is **judge-evaluated**: grade a test against the `## Criteria` below with
`/judge-authoring <test file>` (the generic judge — same engine as `/judge` and `/judge-rulebook`),
read the verdict, fix. Advisory at review time, not a build gate.

Four rules carry it.

## 1. Substitute by ownership

The deciding axis is **who controls the dependency** (Khorikov, *When to Mock*):

- **Managed** — out-of-process but fully ours, reachable only through our app (our repository / the JSON
  store). Talking to it is an *implementation detail*.
- **Unmanaged** — observable outside our system (the `claude`/`codex` CLI, SMTP, a third-party API).
  Talking to it is *observable behavior*.

> Use a **real instance or a hand-rolled fake** for managed dependencies we own. **Mock only unmanaged**
> dependencies, and only behind an interface we own. **Verify state** (the observable result), **never
> interactions** (which calls happened).

Mocking the repository couples the test to *how* the endpoint queries and re-creates the tautology in
rule 4. Reserve mocks for the unmanaged seam — the CLI provider is already faked this way
(`ScriptedProvider`). Hand-rolled fakes (`FakeProjects`, `StubFlow`) over a mocking framework; no
NSubstitute/Moq dependency.

### Three kinds of stand-in (don't blur them)

- **Provisional production impl** — *real* code that ships and gets upgraded later (`JsonRepository`,
  `ProjectSeeder`, the `StorageRoot` default). From a test's view it **is** "the real thing."
- **Test double** — test-only code in `tests/.../Support/`, never ships (`FakeProjects`, `StubFlow`,
  `ScriptedProvider`).
- **Test-env config of a real service** — the real service pointed somewhere safe (`WireApp`'s temp
  `StorageRoot`), not a double.

The fake/real axis is **orthogonal** to provisional/final: "use the real instance" means the current
production impl *even though it's provisional*.

## 2. One swap mechanism

For an HTTP/DI test there is one uniform way to substitute, worth stating because it looks like magic:

- `IServiceCollection` is an ordered list; registering **appends**. Resolving a single service returns the
  **last** descriptor — *last-registration-wins*; the real one is **shadowed**, not removed.
- `WebApplicationFactory.ConfigureTestServices` runs **after** the app's `Program`, so a double registered
  there is guaranteed last → it wins. (Plain `ConfigureServices` runs *before* the app and loses — the trap.)
- Open vs closed generic: production registers open `IRepository<>`; a test registers closed
  `IRepository<Project>`. The container prefers the **exact closed match** — *precedence, not ordering*.

*How* to swap (this) is separate from *whether/what* to swap (rule 1).

## 3. Arrange / Act / Assert

Every body is **Arrange → Act → Assert**: construct inputs + the SUT and seed the fakes (this establishes
the oracle, rule 4), make the **one** call under test, verify the observable result. House no-comments rule:
express AAA **structurally** — three blank-line-separated blocks, one Act line — never `// Arrange` markers.

## 4. No magic values; assert against arranged state

> Every expected value must trace to state the test **arranged** — a constructed object's known state, an
> injected fake's data, or (for structural facts) the project itself (csproj/registry/constant). **Never**
> derive the expectation from the same live source the code under test reads — that's a **tautological
> assertion** (both sides move together; the test can't fail for the reason that matters).

- A literal is *fine* when the test established it: arrange `enemy.Health = 10`, expect `5` after 5 damage —
  `5` is derived from arranged state, not magic.
- A flow test's oracle is the fake's data: seed `[Alpha, Beta]`, assert the endpoint returns `[Alpha, Beta]`
  — that literal changes only when the *test* changes, never from unrelated production code.
- The named failure mode: asserting a `/projects` response against `store.GetAll()` — the same store the
  endpoint reads — proves only "the endpoint echoes its own store." Assert against the project you
  *arranged*, found by its known id.
- Ties to AAA: **Arrange establishes the oracle; Assert derives from Arrange.**

Full decision record + sources: [`PLANS/test-authoring.md`](../../PLANS/test-authoring.md) (the plan this
doc supersedes as the controller).

## Criteria

The semantic checks `/judge-authoring` grades a test against — one judgment each, not mechanical shape:

- **arranged_oracle:** every expected value traces to state the test arranged (a constructed object, an injected fake's data, or the project itself), never re-read from the same source the code under test reads — no tautological assertion
- **state_not_interaction:** assertions check the observable result (return, throw, state, HTTP response), not which calls were made on a dependency
- **ownership_substitution:** managed dependencies the system owns use a real instance or a hand-rolled fake; only unmanaged dependencies are mocked, and only behind an interface we own
- **aaa_shape:** the body reads as Arrange → Act → Assert in blank-line-separated blocks with a single Act call, and carries no `// Arrange` / `// Act` / `// Assert` comments
- **one_behavior:** the test exercises a single behavior — one Act and assertions about that one outcome — rather than bundling several unrelated behaviors
