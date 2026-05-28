# C# Orchestrator — Product Requirements

> **Purpose**: defines *what* the C# orchestrator must do and *why*, separate
> from *how* it's built. The build plan
> ([`csharp-orchestrator-build.md`](csharp-orchestrator-build.md)) covers
> implementation order; the design handover
> ([`csharp-orchestrator-rewrite.md`](csharp-orchestrator-rewrite.md)) covers
> the architecture decisions.
>
> **Status**: requirements pinned 2026-05-28 against the 19 decisions
> captured in the build plan's §1.

---

## 1. Summary

The C# orchestrator is a `net10.0` library + CLI that drives the `claude` and
`codex` agent CLIs against project repositories on **subscription billing**
(Claude Max, ChatGPT Plus/Pro) — not per-token API. It replaces the existing
JS orchestrator (`remote-agents/orchestrator/`) once parity is reached, and
adds two structural things the JS lib lacks:

1. A **named agent layer** (Planner, Documenter, Researcher…) so role-specific
   configuration (model, system prompt, options) lives in one place and is
   reused across flows.
2. **Base classes** designed for extension — both for per-project tooling
   (subclass providers, add validators, register new agents) and for a
   future UI app (events flow through `IEventSink`; cancellation is wired
   in everywhere).

The deliverable is a library + flow runner; the deliverable is **not** a UI
or a server. Those are explicitly downstream.

---

## 2. Problem statement

The JS orchestrator works but has three structural gaps:

1. **No agent abstraction.** Flows call `runClaude(...)` / `runCodex(...)`
   directly and have to repeat model/system-prompt/options at every callsite.
   Real workflows want named roles ("Opus 4.7 + planning system prompt"
   reused across three flows).
2. **No extension story for per-project customization.** Per-project
   primitives (Unity batch-mode compile, Scaffold cold-build workaround)
   bolt on awkwardly because the library has no clear extension surface.
3. **Weak structural guarantees.** TS strict mode catches some classes of
   bugs; sealed-lifecycle and exhaustive switches catch more. For a library
   that drives external CLIs and ships commits to real repos, the stronger
   guarantees matter.

Plus a forward-looking concern:

4. **The UI seam is not designed.** Whenever a UI lands (MAUI Blazor Hybrid
   most likely, see [csharp-orchestrator-rewrite.md §8](csharp-orchestrator-rewrite.md)),
   it needs a clean way to subscribe to live agent events and cancel
   long-running runs. The JS lib emits events but only to a JSONL file; no
   in-process subscription, no cancellation.

---

## 3. Users and use cases

**Primary user**: a single solo developer (the repo owner) running flows
from his Windows laptop today, against multiple Unity projects living under
`C:\Unity\*`. Later, the same flows run from a Hetzner Linux VM (Phase B of
[unity-agent-infrastructure.md](unity-agent-infrastructure.md)).

**Secondary user (future)**: the same developer interacting through a UI
app (MAUI Blazor Hybrid: Windows + Android + web) instead of a CLI.

**Use cases** (in rough order of frequency):

| # | Use case | Today | With C# orchestrator |
|---|---|---|---|
| 1 | Run a flow against a Unity project | `agents run full-review card-framework "..."` (JS) | `agents-dotnet run full-review card-framework "..."` |
| 2 | Write a new flow that composes existing agents | hand-write `flows/<name>.mjs` | hand-write `flows/<name>.cs` with `#:project` directive |
| 3 | Register a new named agent (e.g. `Documenter`) | doesn't exist — re-specify config at each callsite | `agents/Documenter.cs` static factory; reuse anywhere |
| 4 | Add a per-project validator | `validation/<project>.mjs` exporting `validate(...)` | `validation/<Project>Validator.cs` implementing `IValidator` |
| 5 | Debug a run | grep `sessions/<id>/transcript.jsonl` + provider JSONLs | same, with cleaner schema |
| 6 | Customize a provider for one project (e.g. CF needs a custom dialog detector) | not really supported | subclass `ClaudeAgent`, override `DetectStartupDialog` |
| 7 | (future) Drive a flow from a UI button + see live progress | not supported | `ChannelSink` attached, events stream to UI |
| 8 | (future) Cancel a stuck run from a UI button | not supported | `CancellationTokenSource.Cancel()` |

---

## 4. Goals

| # | Goal | Measured by |
|---|---|---|
| G1 | **Parity** with the JS orchestrator on the three example flows | All three flows (`claude-only`, `claude-validate`, `full-review`) produce equivalent session-dir contents and land commits indistinguishable in intent from JS-run commits |
| G2 | **Named agent layer** so role config is reused | At least three named agents (`Planner`, `Documenter`, `Researcher`) defined as static factories; each used by at least one flow without re-specifying model/system-prompt |
| G3 | **Extensible base classes** for per-project tooling | Subclassing `ClaudeAgent` to override a `virtual` hook works without touching the library; new validators slot in via `IValidator` without library changes; new flows compose primitives without new types |
| G4 | **Subscription billing preserved end-to-end** | `claude` and `codex` runs under the C# orchestrator bill against Max/ChatGPT subscriptions, not API credits, verified by post-run `claude auth status` and Codex `token_count.rate_limits.plan_type` |
| G5 | **Sealed lifecycle + exhaustive events** | `Agent.RunAsync` is `sealed`; subclasses cannot bypass `Started`/`Completed`/`Failed` emission; switch expressions on `AgentEvent` warn at compile-time if any case is missed |
| G6 | **UI-ready** (without building UI) | A future UI can attach by registering one extra `IEventSink`; cancellation reaches the PTY child via `CancellationToken` in every async signature; no library API change needed to add UI |
| G7 | **Observable runs by default** | Every flow run produces a `sessions/<id>/` folder containing structured transcript + ingested Claude/Codex provider JSONLs (per-turn tool calls, token usage, rate limits) without flow-author effort |
| G8 | **No silent regressions during coexistence** | Both JS and C# orchestrators can run against the same repo on the same day without one corrupting the other's state |
| G9 | **Card Framework shakedown success** | One end-to-end `full-review` lands a real commit on a Card Framework branch; Codex review verdict = `approve` |

---

## 5. Non-goals (explicit)

| # | Non-goal | Why |
|---|---|---|
| NG1 | Build a UI in this scope | UI is downstream; library must be UI-ready, not UI-bundled |
| NG2 | Ship a tool-definition framework (Anthropic-style structured tools) | `RunCommand` is enough; tool framework is yagni for a CLI-orchestration library |
| NG3 | Memory primitive | CLI-native `CLAUDE.md` / `AGENTS.md` cover the per-run context case; per-flow state is plain file IO |
| NG4 | Agent-calls-agent composition | Composition is the flow's job; primitives are the lego pieces |
| NG5 | Provider fallback to API (LiteLLM HTTP path) | Documented as a possible future option; not in v1 |
| NG6 | Multi-agent orchestration runtime (Mastra/Agno style server) | Stays a library + CLI |
| NG7 | Cross-platform v1 (Linux/macOS) | Windows-only v1; Linux port at Hetzner-VM time |
| NG8 | Backwards-compatible reading of JS-orchestrator session folders | C# `meta.json` is a clean break; JS sessions are legacy |
| NG9 | Live tool-call / token-usage events | Those facts live exclusively in the ingested JSONL files post-run; not in the live event stream |
| NG10 | Visual session-replay viewer | Build only after we've accumulated enough sessions to know what view actually helps |

---

## 6. Functional requirements

### 6.1 Agent layer

- **FR-A1** — Library MUST provide an `abstract class Agent` with a `sealed
  Task<AgentResult> RunAsync(AgentRunRequest, IEventSink, CancellationToken)`
  and a `protected abstract Task<AgentResult> ExecuteAsync(...)`.
- **FR-A2** — `Agent.RunAsync` MUST emit `AgentEvent.Started` before calling
  `ExecuteAsync`, `Completed` on successful return, and `Failed` + rethrow
  on any exception.
- **FR-A3** — Library MUST ship two concrete agents: `ClaudeAgent`
  (PTY-driven, subscription billing) and `CodexAgent` (process-driven).
- **FR-A4** — Both concrete agents MUST be **non-sealed** and expose two
  `virtual protected` hooks in v1: `DetectStartupDialog(string buf)` and
  `IsResponseComplete(string buf, DateTimeOffset lastChunkAt)`.
- **FR-A5** — Agent configuration (name, model, system prompt, options)
  MUST be set via record-init at construction; agents MUST be reusable
  across calls and flows.
- **FR-A6** — `AgentRunRequest` MUST support `SessionId` for resuming a
  prior conversation; `AgentResult` MUST return a `SessionId` the caller
  can pass back on the next call.
- **FR-A7** — Timing constants (`InitialDwellMs`, `IdleThresholdMs`,
  `ExitDwellMs`) MUST be overridable per-agent via a typed
  `ClaudeAgentOptions` record; library MUST provide defaults.
- **FR-A8** — `RunAsync` MUST throw on agent-level failure (transport
  crash, exit-code non-zero with no parseable result). Flow authors
  handle failure via `try/catch`.

### 6.2 Provider behavior

- **FR-P1** — `ClaudeAgent` MUST drive `claude` via Porta.Pty (ConPTY on
  Windows) so the spawned process sees TTYs on both stdin and stdout
  (subscription billing path).
- **FR-P2** — `ClaudeAgent` MUST pass `--session-id <uuid>` on fresh runs
  and `--resume <uuid>` when `AgentRunRequest.SessionId` is set.
- **FR-P3** — `ClaudeAgent` MUST handle the v2.1.x trust dialog and
  bypass-permissions dialog (use substrings `'trust this folder'`,
  `'Is this a project you'`).
- **FR-P4** — `ClaudeAgent` MUST pass system prompt via
  `--append-system-prompt <text>` when `SystemPrompt != null`.
- **FR-P5** — `CodexAgent` MUST drive `codex exec` via `Process` with
  `--sandbox`, `--json`, `-o <tmpfile>` for final-message capture, and
  prompt over stdin via the `-` positional.
- **FR-P6** — `CodexAgent` MUST prepend system prompt to user prompt (no
  native flag) when `SystemPrompt != null`.
- **FR-P7** — Both providers MUST unset their respective API-key env vars
  in spawned child env (Anthropic vars for Claude, `OPENAI_API_KEY` for
  Codex) so the child can't accidentally bill against API.
- **FR-P8** — Both providers MUST return a normalized `AgentResult { Text,
  SessionId, ExitCode, RawOutput }`.

### 6.3 Events and logging

- **FR-E1** — `AgentEvent` MUST be an `abstract record` with exactly five
  `sealed record` cases in v1: `Started`, `StreamChunk`,
  `DialogDismissed`, `Completed`, `Failed`.
- **FR-E2** — `IEventSink.EmitAsync(AgentEvent, CancellationToken)` MUST
  be the only logging surface; library MUST NOT print to console directly.
- **FR-E3** — Library MUST ship `JsonlSink`, `ConsoleSink`, `CompositeSink`,
  and `ProviderJsonlIngestSink`.
- **FR-E4** — `ProviderJsonlIngestSink` MUST copy
  `~/.claude/projects/<encoded-cwd>/<id>.jsonl` and the corresponding
  Codex rollout JSONL into the session dir after each run.

### 6.4 Sessions

- **FR-S1** — Each flow run MUST produce a `sessions/<iso>-<slug>/` folder
  containing `meta.json`, `prompt.txt`, `transcript.jsonl`, and (post-T0)
  one ingested provider JSONL per agent invocation.
- **FR-S2** — `meta.json` MUST contain at minimum: `id`, `flowName`,
  `projectName`, `projectDir`, `userPrompt`, `startedAt`, `endedAt`,
  `durationMs`, `result`.
- **FR-S3** — `meta.json` schema is a clean break from JS; library MUST
  NOT support reading JS-shaped session folders.

### 6.5 Primitives

- **FR-Pr1** — `GitOps` MUST provide `DiffAsync`, `CommitAsync`,
  `PushAsync`, `IsDirtyAsync`, `CurrentBranchAsync`, `AddAsync`. `AddAsync`
  MUST refuse `-A` / `--all`. `PushAsync` MUST refuse `--force` to
  `main`/`master`.
- **FR-Pr2** — `RunCommand.ExecuteAsync` MUST return `{ ExitCode, Stdout,
  Stderr, DurationMs }` and accept `CancellationToken`.
- **FR-Pr3** — `FsDiff` MUST provide `Snapshot` + `Diff` over a project
  dir, honoring a `SkipDirs` list at least covering `.git`,
  `node_modules`, `bin`, `obj`, Unity's `Library`/`Temp`/`Logs`.
- **FR-Pr4** — `ProjectRegistry.Resolve(name)` MUST read
  `<repo>/projects.json` and return an absolute project path.
- **FR-Pr5** — `SubscriptionGuard.ThrowIfApiKeysSet()` MUST throw if any
  of `ANTHROPIC_API_KEY`, `CLAUDE_API_KEY`, `OPENAI_API_KEY` are set in
  the parent env; MUST also run `claude --version` and `codex --version`
  once at flow start and throw with a clear message if either is missing.

### 6.6 Validation

- **FR-V1** — Library MUST define `IValidator` with `Task<ValidationResult>
  ValidateAsync(string projectDir, AgentResult? lastAgentResult,
  CancellationToken)`.
- **FR-V2** — `ValidationResult` MUST be `(bool Ok, string Summary, string
  Errors)`.
- **FR-V3** — Library MUST NOT prescribe validation logic; validators are
  per-project.

### 6.7 Flows and CLI

- **FR-F1** — Flows MUST be authorable as `.NET 10` file-based programs
  with `#:project ../RemoteAgents/RemoteAgents.csproj`.
- **FR-F2** — `agents-dotnet` CLI MUST provide `list` and `run <flow>
  [...args]` subcommands; `run` spawns `dotnet run flows/<flow>.cs` with
  inherited stdio.

### 6.8 Cancellation

- **FR-C1** — Every public async method in the library MUST accept
  `CancellationToken` (default `default`).
- **FR-C2** — Cancellation of `Agent.RunAsync` MUST terminate the spawned
  child process (PTY or Process) within 5 seconds.

---

## 7. Non-functional requirements

| # | Requirement | Target |
|---|---|---|
| NFR1 | `claude` PTY run latency vs JS baseline | Within 20% of the JS orchestrator's median end-to-end time for `full-review` |
| NFR2 | Flow startup overhead (process spawn + JIT + first useful work) | < 3 seconds on the dev laptop |
| NFR3 | Subscription path verified per run | Stage 3 of the smoke test (auth status JSON) MUST be reproducible against any new release |
| NFR4 | Memory footprint | Library + a running flow should fit in < 200 MB resident (excludes child CLIs) |
| NFR5 | Test coverage | Every primitive + the `Agent` lifecycle has a focused xUnit test; aim for assertions covering the contract, not line count |
| NFR6 | Build cleanliness | `dotnet build` MUST be warning-free; `nullable` enabled in all projects |

---

## 8. User scenarios (concrete walks)

### 8.1 "I want a doc commit on Card Framework"

```
> agents-dotnet run full-review card-framework "Add XML docs to public API on CardSpawner"
```

Flow: `requireSubscription()` → resolve project → start session → check
dirty → snapshot files → `planner.RunAsync(prompt)` → validate → if fail,
`planner.RunAsync(fix prompt, sessionId=...)` → snapshot after → diff →
`reviewer.RunAsync(review prompt + diff)` → if `approve`, commit + (push if
flagged) → end session.

Session dir contains:
- `meta.json` with `result: "shipped"`
- `prompt.txt` with the original ask
- `transcript.jsonl` with `Started`, `Completed` per agent, plus the flow's
  own structured events (`validate`, `diff`, `end`)
- `claude-turn-1.jsonl` (and `-2.jsonl` if a fix loop ran) — tool calls,
  edits, token usage
- `codex-review.jsonl` — review reasoning + verdict

### 8.2 "I want a new agent for refactoring"

`agents/Refactorer.cs`:

```csharp
public static class Refactorer
{
    public static Agent Create() => new ClaudeAgent
    {
        Name = "refactorer",
        Model = "claude-opus-4-7",
        SystemPrompt = File.ReadAllText("agents/prompts/refactorer.md"),
        Timing = new ClaudeAgentOptions { IdleThresholdMs = 12_000 },
        Options = new Dictionary<string, object> { ["permissionMode"] = "acceptEdits" },
    };
}
```

Used in any flow without re-specifying config. Done.

### 8.3 "Card Framework needs a custom startup-dialog handler"

`agents/CardFrameworkClaude.cs`:

```csharp
public sealed class CardFrameworkClaudeAgent : ClaudeAgent
{
    protected override string? DetectStartupDialog(string buf)
        => buf.Contains("Custom CF dialog text") ? "cf-dialog" : base.DetectStartupDialog(buf);
}
```

Lifecycle stays in `Agent.RunAsync`; subclass only changes the one thing
it needs to.

### 8.4 (future) "Click Run in the UI, watch live output, click Stop"

UI registers a `ChannelSink` and a `CancellationTokenSource`. Calls
`agent.RunAsync(req, sink, cts.Token)`. Events arrive in the channel; UI
component renders them. Stop button calls `cts.Cancel()`; library
terminates the PTY child within 5s. No library changes needed.

---

## 9. Success metrics

| Metric | Target | Measured at |
|---|---|---|
| All three example flows pass acceptance gate | 100% | end of step 11 |
| Card Framework shakedown lands a commit with Codex `approve` | yes | end of step 15 |
| `transcript.jsonl` + ingested JSONLs cover every run | 100% of runs | continuous |
| Subscription billing intact post-run | 100% (Stage 3 check) | continuous |
| JS orchestrator retirable | yes | after step 15 |
| Cancellation kills a stuck `claude` PTY within 5s | yes | xUnit integration test |

---

## 10. Constraints and assumptions

**Constraints:**
- Windows-only v1 (Q12). Linux port deferred to Hetzner-VM time.
- `.NET 10` SDK required on every machine that runs flows (file-based
  programs).
- `claude` and `codex` CLIs must be on PATH (`SubscriptionGuard` verifies).
- `Porta.Pty 1.0.7` is the pinned PTY backend. Version drift is a known
  risk.

**Assumptions:**
- Claude `--session-id <uuid>` passthrough continues to be honored (smoke
  test confirmed).
- Subscription-billing path stays available (TTY-on-both-sides heuristic
  unchanged through Claude v2.x).
- Codex `--json` event schema remains parseable (we scan for any of
  `thread_id`/`session_id`/`sessionId`).
- The user (single developer) is the only consumer until UI lands; no
  multi-user concerns in v1.

---

## 11. Open questions deferred from this PRD

These were not load-bearing enough for the grilling pass, but should be
revisited if they become real:

- **CLI arg parsing style in flows** — `args[]` direct vs.
  `System.CommandLine`. Defaulting to `args[]` for v1 (matches JS lib
  simplicity). Lift to `System.CommandLine` if flows accumulate complex
  arg surfaces.
- **Default permission mode per named agent** — `Planner` and `Documenter`
  will likely want `acceptEdits`; a future `Reviewer` will want `plan`.
  Decided per-agent at factory time, not library-level.
- **Sidecar prompt loading semantics** — eager (`File.ReadAllText` at
  factory call) vs. lazy (read on first `RunAsync`). Eager is simpler; do
  that. Revisit if hot-reloading prompts becomes useful.
- **Codex `--dangerously-bypass-approvals-and-sandbox`** — flag is set
  because `--sandbox` controls file access. Worth a comment in the C#
  port explaining why we use both, and revisit if Codex changes the
  semantics.

---

## 12. Pointers

| Topic | Location |
|---|---|
| Build plan (how + when) | [`csharp-orchestrator-build.md`](csharp-orchestrator-build.md) |
| Design handover (architecture decisions) | [`csharp-orchestrator-rewrite.md`](csharp-orchestrator-rewrite.md) |
| JS orchestrator architecture | [`remote-agents/orchestrator/docs/architecture.md`](../remote-agents/orchestrator/docs/architecture.md) |
| JS orchestrator usage | [`remote-agents/orchestrator/docs/usage.md`](../remote-agents/orchestrator/docs/usage.md) |
| Logging plan (T0 ingest) | [`remote-agents/research/logging-and-telemetry.md`](../remote-agents/research/logging-and-telemetry.md) |
| C# validation result | [`remote-agents/research/csharp-rewrite-validation.md`](../remote-agents/research/csharp-rewrite-validation.md) |
| Larger infrastructure plan | [`unity-agent-infrastructure.md`](unity-agent-infrastructure.md) |
