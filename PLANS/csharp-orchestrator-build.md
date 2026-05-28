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

| Question | Answer |
|---|---|
| Flow file format | `.NET 10` file-based programs (`.cs` files with `#:project` directive) |
| UI direction | Defer / UI-agnostic. Library is in-process; UI attaches later via `ProjectReference` (MAUI) or HTTP wrapper (Tauri). |
| Validator API | Typed `IValidator` interface |
| JS retirement | Run in parallel; retire only after C# ships 3 flows + 1 real Unity-project shakedown |

These are now load-bearing — the plan below assumes them.

---

## 2. Architecture summary

Four layers, bottom-up:

1. **Primitives** — static helpers / records. Dumb verbs the flow author calls
   directly: `GitOps`, `RunCommand`, `FsDiff`, `ProjectRegistry`,
   `SubscriptionGuard`, `Session`, `PtyExtensions`.
2. **Agents** — `abstract class Agent` with `sealed RunAsync` + abstract
   `ExecuteAsync`. Two concrete subclasses ship in the library: `ClaudeAgent`
   (Porta.Pty) and `CodexAgent` (Process). Downstream code configures them by
   property init or subclasses for project-specific overrides.
3. **Events / sinks** — `abstract record AgentEvent` with sealed cases.
   `IEventSink.EmitAsync` is the only logging surface. Built-in sinks:
   `JsonlSink`, `ConsoleSink`, `CompositeSink`,
   `ProviderJsonlIngestSink` (T0 from the logging plan). UI attaches later as
   another sink (e.g. `ChannelSink`).
4. **Per-project tooling** — convention, not framework. `agents/<Name>.cs`
   static factories, `validation/<Project>Validator.cs` implementing
   `IValidator`, `flows/<name>.cs` file-based programs. Project-specific
   primitives live next to their consumers, not under `RemoteAgents/`.

---

## 3. Repo layout

```
remote-agents-dotnet/                # sibling of remote-agents/ (JS lives on)
├── RemoteAgents.sln
├── RemoteAgents/                    # the library (net10.0)
│   ├── RemoteAgents.csproj
│   ├── Agents/
│   │   ├── Agent.cs                 # abstract base, sealed RunAsync
│   │   ├── ClaudeAgent.cs           # sealed (or virtual ExecuteAsync for subclasses)
│   │   ├── CodexAgent.cs
│   │   ├── AgentRunRequest.cs       # record
│   │   └── AgentResult.cs           # record
│   ├── Events/
│   │   ├── AgentEvent.cs            # abstract record + sealed cases
│   │   ├── IEventSink.cs
│   │   ├── JsonlSink.cs
│   │   ├── ConsoleSink.cs
│   │   ├── CompositeSink.cs
│   │   └── ProviderJsonlIngestSink.cs
│   ├── Sessions/
│   │   ├── Session.cs
│   │   └── SessionMeta.cs
│   ├── Primitives/
│   │   ├── GitOps.cs
│   │   ├── RunCommand.cs
│   │   ├── FsDiff.cs
│   │   ├── ProjectRegistry.cs
│   │   └── SubscriptionGuard.cs
│   ├── Validation/
│   │   ├── IValidator.cs
│   │   └── ValidationResult.cs
│   └── Pty/
│       └── PtyExtensions.cs         # ExitCodeOrNull et al.
├── flows/                           # .NET 10 file-based programs
│   ├── claude-only.cs
│   ├── claude-validate.cs
│   └── full-review.cs
├── agents/                          # named agent registrations
│   ├── Planner.cs                   # static factory → ClaudeAgent
│   ├── Documenter.cs
│   ├── Researcher.cs                # CodexAgent
│   └── prompts/                     # sidecar system prompts
│       ├── planner.md
│       ├── documenter.md
│       └── researcher.md
├── validation/                      # per-project validators
│   ├── OrchestratorValidator.cs     # Roslyn syntax-only parse
│   ├── CardFrameworkValidator.cs    # Unity batch-mode (added later)
│   └── ScaffoldValidator.cs
├── bin/
│   └── agents-dotnet.cs             # CLI shim, file-based program
├── sessions/                        # gitignored
├── projects.json                    # short-name → absolute-path
└── README.md
```

---

## 4. Build order

Each step is a self-contained landing. Estimates assume familiarity with
.NET 10.

| # | Step | Effort | Done when |
|---|---|---:|---|
| 1 | Scaffold `remote-agents-dotnet/` — solution, `RemoteAgents.csproj` targeting `net10.0`, nuget refs (`Porta.Pty 1.0.7`, `Microsoft.Windows.Console.ConPTY 1.24`), `.gitignore` for `bin/obj/sessions/` | 0.5d | `dotnet build` clean |
| 2 | **Primitives layer** — port `GitOps`, `RunCommand`, `FsDiff`, `ProjectRegistry`, `SubscriptionGuard`, `PtyExtensions`. Each gets a unit test. Carry forward the `git add -A` refusal and `push --force` to `main` refusal from the JS lib. | 1d | xUnit suite green; `projects.json` resolves against the existing file |
| 3 | **Sessions + events** — `Session`, `AgentEvent` (Started/StreamChunk/DialogDismissed/ToolCall/TokenUsage/Completed/Failed), `IEventSink`, `JsonlSink`, `ConsoleSink`, `CompositeSink` | 0.5d | a hand-written test flow writes a `sessions/<id>/` folder identical in shape to a JS-orchestrator session |
| 4 | **Agent base** — `Agent` abstract class with sealed `RunAsync` lifecycle (Started → ExecuteAsync → Completed/Failed), `AgentRunRequest`/`AgentResult` records, `IValidator`/`ValidationResult` | 0.5d | a stub `EchoAgent` (subclass overriding `ExecuteAsync` to return the prompt) runs end-to-end and emits the right events |
| 5 | **`ClaudeAgent`** — port `claudeProvider.js` (~150 LoC). Build `claude` argv (`--session-id`/`--resume`, `--permission-mode`, `--append-system-prompt`, `--model`), spawn via Porta.Pty (`cmd.exe /c claude`), detect+dismiss startup dialog (use the JS substrings: `'trust this folder'`, `'Is this a project you'`), idle-wait, send `/exit\r`, return `AgentResult`. Wrap `ExitCode` accesses in `ExitCodeOrNull`. Unset `ANTHROPIC_API_KEY`/`CLAUDE_API_KEY` in spawned env. | 1.5d | reproduces the dotnet-pty-smoke result: `--session-id` honored, PONG round-trips, exit 0, subscription billing intact |
| 6 | **`CodexAgent`** — port `codexProvider.js` (~150 LoC). System prompt prepended to user prompt (no native flag). `codex exec [resume <id>] --cd <dir> -o <tmpfile> --sandbox <opt> --dangerously-bypass-approvals-and-sandbox --json --model <m> -`. Pipe prompt via stdin. Parse JSONL stdout for `thread_id`/`session_id`. Read final message from tmpfile. | 1d | session id round-trips; review prompt against a fixed diff returns a parseable verdict |
| 7 | **`ProviderJsonlIngestSink`** (T0 from logging plan) — after each agent run, copy `~/.claude/projects/<encoded-cwd>/<id>.jsonl` and `~/.codex/sessions/YYYY/MM/DD/rollout-*-<id>.jsonl` into the session dir as `claude-turn-N.jsonl` / `codex-turn-N.jsonl`. Encoded cwd = path with `\`, `/`, `:` → `-`. | 0.5d | a session dir contains the full tool-call timeline for each agent invocation |
| 8 | **First flow: `flows/claude-only.cs`** — `.NET 10` file-based program with `#:project ../RemoteAgents/RemoteAgents.csproj`. Parity with [claude-only.mjs](remote-agents/orchestrator/flows/claude-only.mjs). | 0.5d | `dotnet run flows/claude-only.cs <project> "<prompt>"` produces the same session-dir shape as the JS version |
| 9 | **`OrchestratorValidator`** — implements `IValidator`. Walks `remote-agents-dotnet/**/*.cs`, runs a Roslyn syntax-only parse (`CSharpSyntaxTree.ParseText` + check diagnostics), returns `ValidationResult { Ok, Summary, Errors }`. Equivalent to the `node --check` walker in [orchestrator.mjs](remote-agents/orchestrator/validation/orchestrator.mjs). | 0.5d | catches a deliberately broken `.cs` file; passes against the current tree |
| 10 | **Second flow: `flows/claude-validate.cs`** — Claude run → validate → fix-loop (max 3 attempts) → done. Parity with [claude-validate.mjs](remote-agents/orchestrator/flows/claude-validate.mjs). | 0.5d | introduces a syntax error on purpose, flow recovers within 3 attempts |
| 11 | **Third flow: `flows/full-review.cs`** — Claude → validate → Codex review → commit → optional push. Parity with [full-review.mjs](remote-agents/orchestrator/flows/full-review.mjs). | 1d | end-to-end run lands a commit on a throwaway branch, comparable to the JS baseline at `14b5cc8` |
| 12 | **Three named agents** — `Planner.Create()` (Claude Opus 4.7 + plan prompt), `Documenter.Create()` (Claude Haiku + doc prompt), `Researcher.Create()` (Codex 5.3 + research prompt). System prompts as sidecar `.md` files loaded via `File.ReadAllText`. | 0.5d | each runs against a trivial prompt; agent registrations are <30 lines each |
| 13 | **CLI shim** — `bin/agents-dotnet.cs` file-based program. Subcommands: `list`, `run <flow> [...args]`. Spawns `dotnet run flows/<name>.cs -- ...args` with `stdio: inherit`. Matches the JS [agents.js](remote-agents/orchestrator/bin/agents.js) ergonomics. | 0.5d | `agents-dotnet run full-review <project> "<prompt>" --push` works |
| 14 | **Docs** — port `architecture.md` + `usage.md` from `remote-agents/orchestrator/docs/`. Update for C# idioms (records, `IEventSink`, `Agent` subclassing). | 1d | a fresh reader can write a new flow + validator without reading the JS lib |
| 15 | **Real Unity-project shakedown** — `CardFrameworkValidator` runs Unity batch-mode compile. End-to-end `full-review` against Card Framework with a small documentation task. | 1d | a commit lands on a Card Framework feature branch; session dir contains full Claude + Codex transcripts |

**Total**: 11 working days. Plan for **2 weeks** with buffer for at least one
post-implementation refactor pass once we see what the abstraction feels like
in practice.

---

## 5. Migration & coexistence policy

- JS orchestrator at `remote-agents/orchestrator/` stays untouched and remains
  the source of truth.
- C# orchestrator goes in `remote-agents-dotnet/` alongside it.
- No flag day. Both can run; flow authors pick which one based on
  capabilities at any given moment.
- **Retirement trigger**: C# has shipped real commits via all three example
  flows (`claude-only`, `claude-validate`, `full-review`) AND has done at
  least one end-to-end run against a real Unity project (Card Framework or
  Scaffold). At that point: mark JS deprecated in its README; keep the code
  for one release cycle as a rollback path; remove after first stable C# run
  on the Hetzner VM.
- Bugs surfaced in the JS lib during the rewrite (e.g. the trust-dialog
  wording, `Porta.Pty.ExitCode` gotcha) should still be backported to JS if
  they apply — see the [csharp-rewrite-validation](remote-agents/research/csharp-rewrite-validation.md)
  notes.

---

## 6. UI attachment seam (for later)

The plan is UI-agnostic by decision, but the seam is explicit:

- All agent activity flows through `IEventSink.EmitAsync(AgentEvent)`.
- Built-in sinks write to disk / console. To add a UI, write a
  `ChannelSink` (using `System.Threading.Channels`) that publishes events to
  whatever the UI is observing.
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
4. **Customize an existing provider per-call** — pass `Options` in the
   `Agent` ctor (e.g. `{ ["permissionMode"] = "plan" }`).
5. **Add a primitive** — add to `RemoteAgents/Primitives/`. Only if more
   than one flow would want it. Static, no state, no inheritance.
6. **Subclass `ClaudeAgent` or `CodexAgent`** — when a project needs to
   override `ExecuteAsync` (e.g. extra dialog detection, project-specific
   post-processing). Should be rare.
7. **Add a new provider** — subclass `Agent` directly. Match the contract:
   `ExecuteAsync` must end by returning a populated `AgentResult` and may
   emit any `AgentEvent` cases during execution.

---

## 8. Risks and unknowns going in

| Risk | Mitigation |
|---|---|
| Porta.Pty version drift breaks Claude's TUI rendering | Pin to `1.0.7`. Smoke test in step 5 catches regressions early. Fallback: subclass and replace transport. |
| Claude v2.2+ changes trust-dialog wording again | Keep the substring list short and documented. Set `AGENTS_DEBUG=1` equivalent (`REMOTE_AGENTS_DEBUG=1`) to dump raw PTY for diagnosis. |
| Codex JSON event schema changes (`thread_id` → `session_id` rename has already happened once) | Parse defensively: scan for any of `thread_id`, `session_id`, `sessionId`. Same approach as the JS provider. |
| .NET 10 file-based program tooling immaturity | The smoke test confirmed it works in Nov 2025. If it ever breaks, fall back to single-csproj-per-flow. |
| Roslyn-based validator misses things `node --check` would catch (or vice versa) | Validators are per-language; this is fine. Validator failures should be a strict superset of "compile would fail." |
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
