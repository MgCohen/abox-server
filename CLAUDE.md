# CLAUDE.md

Index for agents working in this repo. Keep it short — it routes to the
canonical docs rather than restating them.

## Talking to the owner

- Direct and instructional. No prose padding, no preamble/postamble.
- Prefer tables and diagrams over paragraphs.
- Don't close turns with CI / policy-guard / `send_later` / PR-watching
  boilerplate. Raise those only when asked or when actively watching a PR.

## What we're doing

Building the server/API behind **A.Box — an agent harness workspace** (.NET 10):
it wraps LLM agents in deterministic structure (workflows, document/spec
enforcement, evaluators, guardrails) so agents get maximum guidance.
**Orchestration is one capability, not the whole product.**

This is a **rebuild of internals, not behavior**: if a user can't tell the
difference in what the system *does*, the rebuild succeeded. We build in **12
layers (L1→L12)**, walking-skeleton-first.

> **The Blazor UI lives in a separate client repo — it is NOT rebuilt here.** This
> repo is the **server/API** the existing client consumes. Don't scaffold a UI here;
> wire contracts in `*.Contracts` are the seam the client binds. (The `prototype/ui/`
> Blazor is reference-only, like the rest of `prototype/`.)

Source of truth, in order:

- **Constitution (behavior):** [`design/behavioral-oracle.md`](design/behavioral-oracle.md)
  — Tier A invariants you MUST honor; Tier B prototype notes you must NOT follow
  unless we make a fresh, explicit decision. Cite the Tier-A item when you rely on it.
- **Specs + plan:** [`PLANS/rebuild/`](PLANS/rebuild) — `01-feature-map.md`
  (capabilities, WHAT/WHY), `02-prd.md` (EARS requirements + R-SPINE/R-ARCH
  rules), `03-implementation-plan.md` (layer architecture + L1→L12 build order).
  The plan's "Current state" + done-when gates are authoritative for progress.
- **Decisions (ADRs):** [`design/adr/`](design/adr) — focused records for choices
  that outlive a single layer. `0001` fixes the flow catalog / config / context model.

## `prototype/` is a REFERENCE, not source of truth

The original code is quarantined under [`prototype/`](prototype) (still builds &
runs). It is a **spike / behavioral reference only.** Behavior is locked by the
oracle and specs — `prototype/` is merely *how the prototype happened to do it*,
which is exactly what we're re-authoring.

- **Never treat `prototype/` code as authoritative.** Don't copy-paste from it.
- **Port the few hard-won bits deliberately**, citing the oracle item (PTY/ConPTY
  choreography, Claude JSONL path/schema, subscription key-scrub, anti-zombie
  teardown). Copy with intent — don't drag the surrounding ergonomics along.
- Deleted at **L12** once parity (PRD AC1–AC6) is reached.

## The rebuild lives in `/src` + `/tests`

One unified solution: `ABox.slnx`. Projects: `Core` (generic infra) ←
`ABox` (the orchestrator — `Agents/`, `Steps/`, `Flows/` as folders, not
separate assemblies); `Hosting` + `Host` compose; `Contracts` holds shared wire
DTOs. An assembly boundary exists only where it earns enforcement or reuse — see
`PLANS/rebuild/03-implementation-plan.md` § Assembly layout.

**Build & test:**
```
dotnet build ABox.slnx        # the product/IDE solution (src/** + central tests)
dotnet test  dirs.proj        # the FULL suite — central + every co-located feature assembly
```
`dirs.proj` (MSBuild traversal) is the test-discovery seam, not `ABox.slnx`: it globs every
`*.Tests.csproj`, so a new feature's tests run with no solution edit. CI builds & tests `dirs.proj`.

**Tests are Rulebooks.** Each test *type* is a *Rulebook* whose `### ` headers are guarantees enforced
1:1/1:N by `[Rule]` facts and a `ParityGuard` — a test never lands without the Rule it proves.
**Tests are co-located with their owner** ([`PLANS/test-colocation.md`](PLANS/test-colocation.md)): a
feature's `Unit`/`Wire`/`E2E`/`Live` live under `src/<…>/<Owner>/Tests/` in `ABox.<Owner>.Tests`; only the
ownerless types (`Arch`/`Structure`/`Docs` + `Meta`) and the shared `Harness`/`Templates`/`Fixtures` stay
under `tests/`. Adding a test → **`test-rulebook`** skill; standing up a feature's test assembly →
**`new-feature-tests`** skill. Front door: [`tests/README.md`](tests/README.md).

## Repo controls (agent guardrails)

This repo protects its **enforcement surface** — the test harness, ADRs, CI, and
build config — from any agent. One policy ([`governance/protected-paths`](governance/protected-paths)),
many enforcers (CI `policy-guard`, git hooks, a Claude `PreToolUse` deny). Editing a
protected path is a deliberate, reviewed act: route it through a PR (don't disable
the block; `ABOX_ALLOW_PROTECTED=1` is a logged local override, CI re-checks). Front
door: [`governance/README.md`](governance/README.md); the why: [`ADR 0010`](design/adr/0010-agent-repo-controls.md).

**You act as the bot `ABox-Agent` — never as the owner.** Use only the credentials this
session was given. A permission wall — protected path, required review, blocked merge to
`main` — is by design: stop and ask the owner to act, don't work around it.

## Code standards

Judgment-call rules we operate by. Mechanical style (formatting, naming) moves
into `.editorconfig` later. Applied **going forward**; the codebase was swept to
the no-comments rule at L3, so existing files already conform.

**Architecture / spine**

- **YAGNI / least mechanism.** Build for the requirement in front of you — no
  speculative abstraction, config, or extensibility "for later." Add the
  abstraction on the *second* real use, not the first. (The assembly-wall
  collapse is the worked example.) **Exception: basic infrastructure that defines
  the repo's architecture** — the structural guardrails that keep drift out (arch
  rulebook, placement guards, the test taxonomy) earn their place on the *first*
  use, because their whole job is to exist before the second use slips through.
- **DI services over statics.** Construct collaborators from the container; no
  hidden static singletons.
- **Results own their display** via `ToString()` — no per-call `summarize` lambda.
- **Test doubles live with the test that uses them.** Fakes and stubs stay local
  to the consuming test; promote to a shared location only when genuinely reused
  (a shared harness/fixture).
- **Throw actionable errors; never swallow them silently.** Messages say what to
  do; an intentional ignore gets a one-line *why*.
- **Label provisional/scaffolding code as provisional** (e.g. `DelayStep` / the stub
  flow) so it's never mistaken for settled design.
- **Per layer:** warning-free build + green tests + behavior verified (run it,
  not just compile) + one coherent commit. Nullable on, warnings-as-errors,
  file-scoped namespaces, net10.0.
- Honor the PRD's spine + architecture rules (R-SPINE-1/2, R-ARCH-1/2/3) and
  non-goals — validators are Steps, no `new Agent()` in composition,
  contracts/guards live with their layer.

**Craft / quality**

- **Prefer deleting to adding.** No dead or commented-out code — delete it; git
  remembers.
- **Clarity over cleverness.** Optimize for the next reader: obvious names,
  straight-line logic.
- **Make illegal states unrepresentable.** Lean on the type system — non-null,
  records, enums, small value types — so bad states don't compile.
- **Single type per file** — except nested types and a generic + its companion.
- **No comments.** Code carries its meaning through names and structure — no
  XML-doc summaries, no narration of what the code already says, no section
  banners. Exactly two comments are allowed: (a) a one-line non-obvious *why* the
  code genuinely cannot express, and (b) an oracle / Tier-A citation on a ported
  tricky bit. Reaching for anything else means rename or restructure instead.
- **Small, focused methods and classes.**
