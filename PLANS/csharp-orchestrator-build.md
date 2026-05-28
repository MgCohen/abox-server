# C# Orchestrator Build Plan

> **Purpose**: ordered implementation plan for the C#/.NET 10 rewrite of the
> JS orchestrator. Derived from the design context in
> [`csharp-orchestrator-rewrite.md`](csharp-orchestrator-rewrite.md) (read that
> first if you're new to this).
>
> **Status**: design accepted, PTY risk resolved (smoke test passed), open
> decisions confirmed by user (2026-05-28). No C# code written yet.

---

## 1. Confirmed decisions

The handover doc left open decisions; the grilling pass on 2026-05-28 pinned
them down. **These are load-bearing for the plan below.**

| # | Question | Decision |
|---|---|---|
| 1 | Flow file format | `.NET 10` file-based programs (`.cs` files with `#:project` directive) |
| 2 | UI direction | Defer / UI-agnostic. Library in-process; UI attaches later via `ProjectReference` (MAUI) or HTTP wrapper (Tauri). |
| 3 | Validator API | Typed `IValidator` interface |
| 4 | JS retirement | Run in parallel; retire only after C# ships 3 flows + Card Framework shakedown |
| 5 | Streaming model | Sinks-only. `RunAsync` returns `Task<AgentResult>`. Events flow through `IEventSink`. UI attaches via `ChannelSink`. No `IAsyncEnumerable<AgentEvent>` return. |
| 6 | Event variants | Live events only: `Started`, `StreamChunk`, `DialogDismissed`, `Completed`, `Failed`. Tool calls / token usage / rate limits live exclusively in the ingested provider JSONL files. |
| 7 | Provider class shape | `ClaudeAgent` / `CodexAgent` are non-sealed with a small set of `virtual` protected hooks. `Agent.RunAsync` remains `sealed`. |
| 8 | Virtual hooks v1 | Only `DetectStartupDialog(buf)` and `IsResponseComplete(buf, lastChunkAt)` are `virtual` in v1. Others stay `private`; lift later if a real subclass need shows up. |
| 9 | Cancellation | `CancellationToken` in every async signature from day one. |
| 10 | Error propagation | `RunAsync` emits `Failed` then re-throws. Flow authors `try/catch` or let it crash. |
| 11 | Timing constants | Typed `ClaudeAgentOptions` record holds `InitialDwellMs`, `IdleThresholdMs`, `ExitDwellMs`. Per-agent override, library provides defaults. |
| 12 | Platform target | Windows-only v1. Hetzner-time Linux port is a known follow-up tax. |
| 13 | Test framework | `RemoteAgents.Tests/` sibling project in the same solution, xUnit. |
| 14 | `projects.json` location | Promoted to `<repo>/projects.json`. Owned by the repo, consumed by both orchestrators during coexistence and by C# alone after retirement. |
| 15 | `sessions/` folder | Each orchestrator keeps its own (`remote-agents-dotnet/sessions/`). JS folder is throwaway after retirement. |
| 16 | `meta.json` schema | C# breaks clean. JS sessions stay legacy; no compat shim. |
| 17 | Subscription guard scope | Checks `ANTHROPIC_API_KEY`, `CLAUDE_API_KEY`, **and** `OPENAI_API_KEY`. Also runs `claude --version` / `codex --version` once at flow start to fail fast on missing binaries. |
| 18 | Step-11 acceptance gate | Plumbing (Claude JSONL + Codex JSONL + transcript.jsonl + commit) **+** Codex verdict = `approve`. |
| 19 | Step-15 shakedown project | Card Framework. Scaffold's cold-build bug makes it contaminated test data. |

---

## 2. Architecture summary

Four layers, bottom-up:

1. **Primitives** — static helpers / records. Dumb verbs the flow author calls
   directly: `GitOps`, `RunCommand`, `FsDiff`, `ProjectRegistry`,
   `SubscriptionGuard`, `Session`, `PtyExtensions`.
2. **Agents** — `abstract class Agent` with `sealed RunAsync` (lifecycle:
   `Started` → `ExecuteAsync` → `Completed`/`Failed`) and `protected abstract
   ExecuteAsync`. Two concrete subclasses ship in the library: `ClaudeAgent`
   (Porta.Pty) and `CodexAgent` (`Process`). Both are **non-sealed** with two
   `virtual` protected hooks for v1 (`DetectStartupDialog`,
   `IsResponseComplete`). Downstream code configures by property init or
   subclasses for project-specific overrides.
3. **Events / sinks** — `abstract record AgentEvent` with sealed cases
   (`Started`, `StreamChunk`, `DialogDismissed`, `Completed`, `Failed` — five
   variants, all live during the run). `IEventSink.EmitAsync` is the only
   logging surface. Built-in sinks: `JsonlSink`, `ConsoleSink`,
   `CompositeSink`, `ProviderJsonlIngestSink` (T0 from the logging plan,
   copies `~/.claude/projects/.../<id>.jsonl` + `~/.codex/sessions/...` into
   the session dir post-run; this is where tool calls, token usage, and rate
   limits live).
4. **Per-project tooling** — convention, not framework. `agents/<Name>.cs`
   static factories, `validation/<Project>Validator.cs` implementing
   `IValidator`, `flows/<name>.cs` file-based programs. Project-specific
   primitives live next to their consumers, not under `RemoteAgents/`.

**Runtime contract:**
- `RunAsync` returns `Task<AgentResult>` and throws on failure (emitting
  `Failed` first). No `IAsyncEnumerable<AgentEvent>` return.
- Every async method takes a `CancellationToken` (default `default`) — UI
  cancel-button, flow-level timeout, and shutdown hooks all work.
- A UI attaches by registering an extra `IEventSink` (typically a
  `ChannelSink` over `System.Threading.Channels`). Library knows nothing
  about UI.

---

## 3. Repo layout

```
<repo>/
├── projects.json                      # ← promoted from orchestrator-local (Q14)
├── remote-agents/                     # JS orchestrator (untouched, retires later)
└── remote-agents-dotnet/              # ← new
    ├── RemoteAgents.sln               # includes both projects below
    ├── RemoteAgents/                  # the library (net10.0)
    │   ├── RemoteAgents.csproj
    │   ├── Agents/
    │   │   ├── Agent.cs               # abstract base, sealed RunAsync
    │   │   ├── ClaudeAgent.cs         # non-sealed, 2 virtual hooks
    │   │   ├── ClaudeAgentOptions.cs  # record: InitialDwellMs, IdleThresholdMs, ExitDwellMs
    │   │   ├── CodexAgent.cs
    │   │   ├── CodexAgentOptions.cs   # record: sandbox, json-stream timeout
    │   │   ├── AgentRunRequest.cs     # record (Prompt, SessionId?, ProjectDir)
    │   │   └── AgentResult.cs         # record (Text, SessionId, ExitCode, RawOutput)
    │   ├── Events/
    │   │   ├── AgentEvent.cs          # abstract record + 5 sealed cases
    │   │   ├── IEventSink.cs
    │   │   ├── JsonlSink.cs
    │   │   ├── ConsoleSink.cs
    │   │   ├── CompositeSink.cs
    │   │   └── ProviderJsonlIngestSink.cs
    │   ├── Sessions/
    │   │   ├── Session.cs
    │   │   └── SessionMeta.cs         # new C# schema (Q16, no JS compat)
    │   ├── Primitives/
    │   │   ├── GitOps.cs
    │   │   ├── RunCommand.cs
    │   │   ├── FsDiff.cs
    │   │   ├── ProjectRegistry.cs     # reads <repo>/projects.json
    │   │   └── SubscriptionGuard.cs   # env vars + binary existence
    │   ├── Validation/
    │   │   ├── IValidator.cs
    │   │   └── ValidationResult.cs
    │   └── Pty/
    │       └── PtyExtensions.cs       # ExitCodeOrNull, etc.
    ├── RemoteAgents.Tests/            # xUnit (Q13)
    │   ├── RemoteAgents.Tests.csproj
    │   ├── Agents/
    │   │   └── AgentLifecycleTests.cs # FakeAgent verifies Started/Failed/Completed
    │   ├── Primitives/
    │   │   ├── GitOpsTests.cs
    │   │   └── FsDiffTests.cs
    │   └── Sessions/
    │       └── JsonlSinkTests.cs
    ├── flows/                         # .NET 10 file-based programs
    │   ├── claude-only.cs
    │   ├── claude-validate.cs
    │   └── full-review.cs
    ├── agents/                        # named agent registrations
    │   ├── Planner.cs                 # static factory → ClaudeAgent
    │   ├── Documenter.cs
    │   ├── Researcher.cs              # CodexAgent
    │   └── prompts/                   # sidecar system prompts
    │       ├── planner.md
    │       ├── documenter.md
    │       └── researcher.md
    ├── validation/                    # per-project validators
    │   ├── OrchestratorValidator.cs   # Roslyn syntax-only parse
    │   └── CardFrameworkValidator.cs  # Unity batch-mode (step 15)
    ├── bin/
    │   └── agents-dotnet.cs           # CLI shim, file-based program
    ├── sessions/                      # gitignored (Q15, per-orchestrator)
    └── README.md
```

---

## 4. Build order

Each step is a self-contained landing. Estimates assume familiarity with
.NET 10.

| # | Step | Effort | Done when |
|---|---|---:|---|
| 1 | Scaffold `remote-agents-dotnet/` — `RemoteAgents.sln`, `RemoteAgents.csproj` and `RemoteAgents.Tests.csproj` targeting `net10.0`, nuget refs (`Porta.Pty 1.0.7`, `Microsoft.Windows.Console.ConPTY 1.24`, `xunit`, `xunit.runner.visualstudio`), `.gitignore` for `bin/obj/sessions/`. Move `projects.json` from `remote-agents/orchestrator/` to `<repo>/projects.json`; update the JS reader to read from the new path so coexistence keeps working. | 0.5d | `dotnet build` clean; `dotnet test` runs zero tests successfully; JS orchestrator still resolves projects |
| 2 | **Primitives layer** — port `GitOps`, `RunCommand`, `FsDiff`, `ProjectRegistry`, `SubscriptionGuard`, `PtyExtensions`. Every public async method takes `CancellationToken`. `SubscriptionGuard` checks `ANTHROPIC_API_KEY`, `CLAUDE_API_KEY`, `OPENAI_API_KEY` env vars AND runs `claude --version` / `codex --version` to fail fast on missing binaries. Carry forward `git add -A` refusal and `push --force` to `main` refusal. xUnit tests for each. | 1d | xUnit suite green; `projects.json` resolves; guard throws on each of the three env vars; guard throws with a clear message when `claude` is renamed off PATH |
| 3 | **Sessions + events** — `Session` (writes new C# schema, no JS compat), `AgentEvent` (5 sealed cases: `Started`/`StreamChunk`/`DialogDismissed`/`Completed`/`Failed`), `IEventSink`, `JsonlSink`, `ConsoleSink`, `CompositeSink`. Every emission path takes `CancellationToken`. | 0.5d | a hand-written test flow writes a `sessions/<id>/` folder with new-schema `meta.json` + `transcript.jsonl`; ordering of events is deterministic |
| 4 | **Agent base + records** — `Agent` abstract class with `sealed RunAsync` lifecycle (Started → ExecuteAsync → Completed on success, Failed + rethrow on exception), `AgentRunRequest`/`AgentResult` records, `ClaudeAgentOptions`/`CodexAgentOptions` records, `IValidator`/`ValidationResult`. xUnit `FakeAgent` test verifies all five event types fire under the right conditions and that exceptions re-throw cleanly. | 0.5d | unit tests for lifecycle invariants pass |
| 5 | **`ClaudeAgent`** — port `claudeProvider.js` (~150 LoC) as a **non-sealed** class. Build `claude` argv (`--session-id`/`--resume`, `--permission-mode`, `--append-system-prompt`, `--model`), spawn via Porta.Pty (`cmd.exe /c claude` — Windows-only, Q12), use the two `virtual` hooks: `DetectStartupDialog(string buf)` (use JS substrings: `'trust this folder'`, `'Is this a project you'`), `IsResponseComplete(string buf, DateTimeOffset lastChunkAt)` (default: 6s idle threshold from `ClaudeAgentOptions.IdleThresholdMs`). Wrap `ExitCode` accesses in `ExitCodeOrNull`. Unset Anthropic env vars in spawned child env. | 1.5d | reproduces dotnet-pty-smoke result: `--session-id` honored, PONG round-trips, exit 0, subscription billing intact; xUnit test for arg-builder ensures `--append-system-prompt` is set iff `SystemPrompt != null` |
| 6 | **`CodexAgent`** — port `codexProvider.js` (~150 LoC) as a **non-sealed** class. System prompt prepended to user prompt (no native flag). `codex exec [resume <id>] --cd <dir> -o <tmpfile> --sandbox <opt> --dangerously-bypass-approvals-and-sandbox --json --model <m> -`. Pipe prompt via stdin. Parse JSONL stdout for `thread_id`/`session_id`/`sessionId`. Read final message from tmpfile. Same Windows-only `cmd.exe /c codex` assumption. Unset `OPENAI_API_KEY` in spawned child env. | 1d | session id round-trips; review prompt against a fixed diff returns a parseable verdict |
| 7 | **`ProviderJsonlIngestSink`** (T0 from logging plan) — after each agent run, copy `~/.claude/projects/<encoded-cwd>/<id>.jsonl` and `~/.codex/sessions/YYYY/MM/DD/rollout-*-<id>.jsonl` into the session dir as `claude-turn-N.jsonl` / `codex-turn-N.jsonl`. Encoded cwd = path with `\`, `/`, `:` → `-`. | 0.5d | a session dir contains the full tool-call timeline for each agent invocation |
| 8 | **First flow: `flows/claude-only.cs`** — `.NET 10` file-based program with `#:project ../RemoteAgents/RemoteAgents.csproj`. Parity with [claude-only.mjs](remote-agents/orchestrator/flows/claude-only.mjs). | 0.5d | `dotnet run flows/claude-only.cs <project> "<prompt>"` produces the same logical session-dir shape as the JS version (schemas differ per Q16, file set matches) |
| 9 | **`OrchestratorValidator`** — implements `IValidator`. Walks `remote-agents-dotnet/**/*.cs`, runs a Roslyn syntax-only parse (`CSharpSyntaxTree.ParseText` + check diagnostics), returns `ValidationResult { Ok, Summary, Errors }`. Equivalent to the `node --check` walker in [orchestrator.mjs](remote-agents/orchestrator/validation/orchestrator.mjs). | 0.5d | catches a deliberately broken `.cs` file; passes against the current tree |
| 10 | **Second flow: `flows/claude-validate.cs`** — Claude run → validate → fix-loop (max 3 attempts) → done. Parity with [claude-validate.mjs](remote-agents/orchestrator/flows/claude-validate.mjs). | 0.5d | introduces a syntax error on purpose, flow recovers within 3 attempts |
| 11 | **Third flow: `flows/full-review.cs`** — Claude → validate → Codex review → commit → optional push. Parity with [full-review.mjs](remote-agents/orchestrator/flows/full-review.mjs). | 1d | **Acceptance gate (Q17)**: session dir contains `claude-turn-N.jsonl` + `codex-review.jsonl` + populated `transcript.jsonl` with the expected event sequence (Started/Completed at minimum, no Failed), AND a commit lands on a throwaway branch, AND the Codex verdict from `codex-review.jsonl`'s final `agent_message` parses as `approve` |
| 12 | **Three named agents** — `Planner.Create()` (Claude Opus 4.7 + plan prompt), `Documenter.Create()` (Claude Haiku 4.5 + doc prompt), `Researcher.Create()` (Codex 5.3 + research prompt). System prompts as sidecar `.md` files loaded via `File.ReadAllText` at factory-call time. | 0.5d | each runs against a trivial prompt; agent registrations are <30 lines each |
| 13 | **CLI shim** — `bin/agents-dotnet.cs` file-based program. Subcommands: `list`, `run <flow> [...args]`. Spawns `dotnet run flows/<name>.cs -- ...args` with `stdio: inherit`. Matches the JS [agents.js](remote-agents/orchestrator/bin/agents.js) ergonomics. | 0.5d | `agents-dotnet run full-review <project> "<prompt>" --push` works |
| 14 | **Docs** — port `architecture.md` + `usage.md` from `remote-agents/orchestrator/docs/`. Update for C# idioms (records, `IEventSink`, `Agent` subclassing). | 1d | a fresh reader can write a new flow + validator without reading the JS lib |
| 15 | **Card Framework shakedown (Q18)** — `CardFrameworkValidator` runs Unity batch-mode compile. End-to-end `full-review` against Card Framework with a small documentation task. **Scaffold is explicitly excluded** as a shakedown target — its `com.scaffold.schemas` cold-build bug makes it contaminated test data; fix that separately. | 1d | a commit lands on a Card Framework feature branch; session dir contains full Claude + Codex transcripts; same acceptance gate as step 11 |

**Total**: 11 working days. Plan for **2 weeks** with buffer for at least one
post-implementation refactor pass once we see what the abstraction feels like
in practice.

---

## 5. Migration & coexistence policy

- JS orchestrator at `remote-agents/orchestrator/` stays untouched (except
  for one read-path change in step 1 to consume `<repo>/projects.json`) and
  remains the source of truth until retirement.
- C# orchestrator goes in `remote-agents-dotnet/` alongside it.
- No flag day. Both can run; flow authors pick which one based on
  capabilities at any given moment.
- **Retirement trigger**: C# has shipped real commits via all three example
  flows (`claude-only`, `claude-validate`, `full-review`) AND has done the
  Card Framework shakedown (step 15) successfully. At that point: mark JS
  deprecated in its README; keep the code for one release cycle as a
  rollback path; remove after first stable C# run on the Hetzner VM.
- The two orchestrators write to separate `sessions/` folders (Q15). JS
  sessions become archival on retirement.
- The two orchestrators use **different `meta.json` schemas** (Q16). A
  future replay viewer will know which orchestrator wrote a session by
  which folder it's in.
- Bugs surfaced in the JS lib during the rewrite (e.g. trust-dialog
  wording, `Porta.Pty.ExitCode` gotcha) should still be backported to JS if
  they apply — see [csharp-rewrite-validation](remote-agents/research/csharp-rewrite-validation.md).

---

## 6. UI attachment seam (for later)

The plan is UI-agnostic by decision, but the seam is explicit:

- All agent activity flows through `IEventSink.EmitAsync(AgentEvent, ct)`.
- Built-in sinks write to disk / console. To add a UI, write a
  `ChannelSink` (using `System.Threading.Channels<AgentEvent>`) that
  publishes events to whatever the UI is observing.
- `CancellationToken` is wired into every async signature (Q9), so a UI
  Stop button is one `CancellationTokenSource.Cancel()` away — no
  retrofit.
- For **MAUI Blazor Hybrid**: UI project takes `ProjectReference` on
  `RemoteAgents.csproj`. Pass `AgentEvent` instances directly to Blazor
  components. No IPC, no JSON boundary, types shared by construction.
- For **Tauri 2 + React**: add a thin `RemoteAgents.Host` ASP.NET project
  that wraps a flow runner in HTTP/JSON-RPC + WebSocket for live events.
  OpenAPI codegen at the Tauri side.
- Either path: **don't change the library's public types** to accommodate
  the UI. If a future change is needed (e.g. making `AgentEvent` more
  serialization-friendly), it's a library decision, not a UI patch.

---

## 7. Extension points (so a new contributor can land a per-project change)

In rough order of "how often you'll touch them":

1. **Write a new flow** — add `flows/<name>.cs` with the `#:project`
   directive. Use the primitives + agents directly. Done.
2. **Write a new validator** — add `validation/<Project>Validator.cs`
   implementing `IValidator`. Wire into a flow.
3. **Register a new named agent** — add `agents/<Name>.cs` static factory.
   Add sidecar prompt to `agents/prompts/<name>.md`.
4. **Customize an existing provider per-call** — pass typed options in the
   `Agent` ctor (e.g. `new ClaudeAgent { Timing = new() { IdleThresholdMs = 30_000 } }`).
5. **Add a primitive** — add to `RemoteAgents/Primitives/`. Only if more
   than one flow would want it. Static, no state, no inheritance. Async
   methods must take `CancellationToken`.
6. **Subclass `ClaudeAgent` or `CodexAgent`** — when a project needs to
   override a `virtual` hook (v1: only `DetectStartupDialog` and
   `IsResponseComplete`). If you need a different hook lifted to
   `virtual`, file a library change — don't reach into `private`.
7. **Add a new provider** — subclass `Agent` directly. Match the contract:
   `ExecuteAsync` must end by returning a populated `AgentResult` and may
   emit any of the five `AgentEvent` cases during execution. Throw on
   failure; the sealed `RunAsync` will emit `Failed` and rethrow.

---

## 8. Risks and unknowns going in

| Risk | Mitigation |
|---|---|
| Porta.Pty version drift breaks Claude's TUI rendering | Pin to `1.0.7`. Smoke test in step 5 catches regressions early. Fallback: subclass and replace transport. |
| Claude v2.2+ changes trust-dialog wording again | `DetectStartupDialog` is `virtual` for exactly this case. Override in a subclass with the new substring; don't tighten the library. |
| Codex JSON event schema changes (`thread_id` → `session_id` rename has already happened once) | Parse defensively: scan for any of `thread_id`, `session_id`, `sessionId`. Same approach as the JS provider. |
| .NET 10 file-based program tooling immaturity | The smoke test confirmed it works in Nov 2025. If it ever breaks, fall back to single-csproj-per-flow. |
| Roslyn-based validator misses things `node --check` would catch (or vice versa) | Validators are per-language; this is fine. Validator failures should be a strict superset of "compile would fail." |
| **Windows-only v1 → Linux port tax at Hetzner time (Q12)** | Acknowledged. Concentrated in `cmd.exe /c` callsites + Porta.Pty Linux path + binary discovery. Estimated 1–2d port when Phase B Hetzner VM lands. |
| C# rewrite stalls and JS bit-rots | Hard cap at 2 weeks. If step 11 hasn't landed by then, regroup before committing further. |

---

## 9. Out of scope (explicitly)

- No tool-definition framework (Anthropic-style). Shell-outs use
  `RunCommand`.
- No memory primitive. CLI-native `CLAUDE.md` / `AGENTS.md` files cover the
  "context per run" case. Per-flow state is plain `File.AppendAllText`.
- No agent-calls-agent composition primitive. Composition is the flow's
  job.
- No provider fallback (LiteLLM HTTP path). Documented as a future option;
  not built in v1.
- No UI in this plan. See §6 for the seam.
- No multi-agent orchestration runtime (Mastra/Agno style). Stays a
  library + CLI.
- No `IAsyncEnumerable<AgentEvent>` return shape (Q5).
- No live tool-call or token-usage events (Q6). Those facts live in the
  ingested JSONL files only.
- No backwards-compatible reading of JS-orchestrator session folders
  (Q16). JS sessions are legacy.
- No Linux/macOS support in v1 (Q12).

---

## 10. Pointers

| Topic | Location |
|---|---|
| Design context (read first) | [`PLANS/csharp-orchestrator-rewrite.md`](csharp-orchestrator-rewrite.md) |
| JS orchestrator architecture | [`remote-agents/orchestrator/docs/architecture.md`](../remote-agents/orchestrator/docs/architecture.md) |
| JS orchestrator usage | [`remote-agents/orchestrator/docs/usage.md`](../remote-agents/orchestrator/docs/usage.md) |
| Logging plan (T0 ingest in step 7) | [`remote-agents/research/logging-and-telemetry.md`](../remote-agents/research/logging-and-telemetry.md) |
| C# validation result + carry-forward findings | [`remote-agents/research/csharp-rewrite-validation.md`](../remote-agents/research/csharp-rewrite-validation.md) |
| Reference smoke test artifact | `C:\Unity\dotnet-pty-smoke\` (outside repo) |
| JS claude provider (port reference for step 5) | [`remote-agents/orchestrator/src/providers/claudeProvider.js`](../remote-agents/orchestrator/src/providers/claudeProvider.js) |
| JS codex provider (port reference for step 6) | [`remote-agents/orchestrator/src/providers/codexProvider.js`](../remote-agents/orchestrator/src/providers/codexProvider.js) |
| Larger infrastructure plan | [`PLANS/unity-agent-infrastructure.md`](unity-agent-infrastructure.md) |
