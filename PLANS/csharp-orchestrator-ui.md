---
type: plan
status: draft
tags: [#ui, #host, #maui, #blazor, #mobile, #tailscale]
---

# C# Orchestrator UI/Host — implementation plan

> **Goal.** Add a mobile + web + desktop front-end to the existing
> `remote-agents-dotnet` orchestrator so I can: open the app on my phone,
> select a project, select a flow, fill in a prompt, press OK, watch live
> output, and respond to any agent question that surfaces. Always-on against
> my Windows laptop, reachable over Tailscale.
>
> **Ordering.** Strictly additive — zero edits to existing `RemoteAgents`
> library, `flows/`, `agents/`, or `validation/` projects. Parallel branch
> (`phase-ui/host-mobile`) so flow/infra work on `phase-a/local-validation`
> keeps moving independently.

---

## Current state (2026-05-28 — branch created)

**Branch**: `phase-ui/host-mobile` (just forked from `phase-a/local-validation`
at `1e76acc cleanup remote-agents/ and harden ClaudeAgent teardown`).

**Resume here**: Phase **C1** — scaffold `ui/` tree and second `.slnx`. C0
(this doc) just landed.

| Phase | Status | Touches existing code? |
|---|---|---|
| C0 — Decision capture + plan doc | ✅ this file | No |
| C1 — `RemoteAgents.Host` (ASP.NET + ChannelSink + FlowRunner + REST/SignalR) | ✅ C1.1–C1.4 landed | No |
| C2 — Interactive-prompt seam (partial — surface + record; answer-back routing blocked on library v2) | 🟡 | No |
| C3 — Tailscale binding + nssm always-on | ⏸ | No (config only) |
| C4 — `UI.Components` Razor lib + `UI.Web` Blazor WASM | ⏸ | No |
| C5 — `UI.Maui` Blazor Hybrid (Win/Android/iOS) | ⏸ | No |
| C6 — Run persistence (SQLite or JSON) | ⏸ | No |

**C2 partial state**: `AgentQuestion`/`NeedsInput` events already flow through `transcript.jsonl` → `ChannelSink` → SignalR with no new Host code (the library does the detection at hook-parse time). The `POST /runs/{id}/respond` endpoint and `Run.PendingResponse` field are scaffolded so the UI can be built against the locked wire shape. **Answer-back routing into a paused agent is deferred to library v2** ([`interaction-modes.md`](interaction-modes.md) Q10): the design hasn't picked between TUI keypress / `--resume` reply / file-poll / pipe yet, so Host can't pick a transport unilaterally without forcing a library change.

**Load-bearing constraints**:
- **Additive only.** No edits to any file under `remote-agents-dotnet/src/`,
  `remote-agents-dotnet/flows/`, `remote-agents-dotnet/agents/`, or
  `remote-agents-dotnet/cli/`. New work lives entirely under
  `remote-agents-dotnet/ui/`.
- **Two solution files.** `RemoteAgents.slnx` stays untouched. New
  `RemoteAgents.UI.slnx` includes the existing projects via path + the new
  `ui/` projects.
- **Subscription billing preserved.** Host never calls Anthropic directly.
  It runs flows (or instantiates `ClaudeAgent` / `CodexAgent` from the
  library) which already enforce subscription billing via PTY (Claude) or
  signed-in CLI (Codex).
- **Tailnet-only.** Host binds to the Tailscale interface; no public ports.
  Phone is already on the tailnet (W1–W3 in the infra plan).
- **Single Razor component library** is the deliverable: same `.razor`
  files render in MAUI on Android/Windows and in WASM on web.

---

## How to execute this plan (executor notes)

Read in this order before doing anything: §2 (decisions), §3 (architecture
in one diagram), §4 (the phases). The parent C# orchestrator architecture
lives in [`../remote-agents-dotnet/docs/architecture.md`](../remote-agents-dotnet/docs/architecture.md) — §10
"UI seam (for later)" is the explicit attach point this plan operationalises.

- **Gate at each phase boundary.** Tick the checkboxes in this file, commit,
  summarise findings, wait for user go-ahead before starting the next.
- **No edits to existing projects, ever.** If a phase appears to need one,
  stop and resurface — either the design needs adjusting or the library
  needs a tiny new seam, which is its own decision.
- **Artifact discipline.** All new code under
  `remote-agents-dotnet/ui/<project>/`. All new plan/decision text under
  `PLANS/`. No scratch dirs.

---

## 1. What we're building

A four-project addition to `remote-agents-dotnet/`:

```
remote-agents-dotnet/
├── src/RemoteAgents/                  UNTOUCHED
├── flows/, agents/, validation/, cli/  UNTOUCHED
├── RemoteAgents.slnx                  UNTOUCHED
└── ui/                                 NEW
    ├── RemoteAgents.Host/             ASP.NET — REST + SignalR
    ├── RemoteAgents.UI.Components/    Razor class lib (shared)
    ├── RemoteAgents.UI.Web/           Blazor WASM, served by Host
    └── RemoteAgents.UI.Maui/          MAUI Blazor Hybrid

remote-agents-dotnet/RemoteAgents.UI.slnx  NEW — second solution
```

End-state UX from the phone:

1. Open app (MAUI Android) or browser tab (`http://<laptop-tailnet>:5050`).
2. Pick project from a list (sourced from `<repo>/projects.json`).
3. Pick flow from a list (sourced from `flows/*.cs`, `smoke-*` hidden).
4. Fill prompt + optional args, press Run.
5. Live log streams in via SignalR — same events the CLI prints.
6. If the agent surfaces an `AgentQuestion`, a modal appears; tap a choice;
   the response routes back into the agent's input.
7. Session links to `sessions/<ts>-<slug>/` for post-run inspection.

---

## 2. Decisions

Confirmed 2026-05-28 in the source conversation. Load-bearing.

| # | Question | Decision |
|---|---|---|
| 1 | Host machine | **Windows laptop, local.** Keeps the "validate locally before cloud spend" rule. Hetzner VM (Phase A4+ in infra plan) stays parked. |
| 2 | UI stack | **MAUI Blazor Hybrid + shared Razor class library + Blazor WASM web.** One Razor codebase, three shells (Android/Windows/iOS + web). |
| 3 | Host transport | **ASP.NET (Kestrel) — REST for control, SignalR for live events.** Sits in front of an in-process `ChannelSink` consuming the existing `IEventSink` interface. |
| 4 | Flow execution model in v1 | **Child process per run.** Host spawns `dotnet run flows/<flow>.cs -- <args>` exactly like the CLI shim does today; tails the per-run `transcript.jsonl` and re-emits each line as a SignalR event. Avoids needing in-process flow loading in v1; lets the library keep its file-based-program convention. (Future v2 may move to in-process `Agent` instantiation for finer control — `Agent.Sink` is `init`, so it slots in without library change.) |
| 5 | Type sharing across HTTP | **Direct DTO sharing.** `RemoteAgents.UI.Components` takes a `ProjectReference` on `RemoteAgents` and reuses `AgentEvent`, `AgentQuestion`, `AgentResult` records as-is. They're already `[JsonPolymorphic]`-annotated. No OpenAPI codegen step. |
| 6 | Authentication v1 | **Tailnet membership.** Host binds to the Tailscale interface only. Tailscale ACL (already in infra plan A4.2) restricts phone → port 5050 only. No user/password layer in v1. |
| 7 | Persistence v1 | **JSON file in `~/.remote-agents/runs.json`** for run metadata (id, project, flow, startedAt, status, sessionDir). Streaming content stays on disk in `sessions/<ts>-<slug>/transcript.jsonl` + provider JSONLs (no re-storage). SQLite upgrade if list grows past ~5K runs. |
| 8 | Always-on shape | **nssm Windows service.** Auto-start on boot. Windows power settings prevent sleep on AC. |
| 9 | Interactive-prompt model | **Consume the existing library seams.** `InteractionMode.Interactive` + `AgentQuestion` + `AgentResult.NeedsInput` already exist in the library (untracked-but-in-progress on the flow track). The Host doesn't need new agent code; it surfaces these events to SignalR clients and accepts responses via `POST /runs/{id}/respond`. |

---

## 3. Architecture

```
┌────────────────────────────────────────────────────────────────┐
│  Phone (MAUI Android)    Tablet/Laptop (MAUI Win)   Browser    │
│      │                       │                        │        │
│      └───────┬───────────────┴────────────────────────┘        │
│              │  HTTPS + SignalR (Tailscale)                    │
└──────────────┼─────────────────────────────────────────────────┘
               │
               ▼
┌────────────────────────────────────────────────────────────────┐
│  RemoteAgents.Host  (ASP.NET Kestrel, nssm-managed)            │
│                                                                │
│  REST:                                                          │
│    GET  /projects     → ProjectRegistry                         │
│    GET  /flows        → enumerate flows/*.cs                    │
│    POST /runs         → FlowRunner.Start                        │
│    POST /runs/{id}/respond  → routes to AgentQuestion handler   │
│    POST /runs/{id}/cancel   → CTS.Cancel                        │
│    GET  /runs         → list (from runs.json)                   │
│                                                                │
│  SignalR Hub: /runs/{id}/events  → ChannelSink → wire           │
│                                                                │
│  Static: /  → Blazor WASM bundle (UI.Web)                       │
└──────────────┬─────────────────────────────────────────────────┘
               │ spawns child process per run
               ▼
┌────────────────────────────────────────────────────────────────┐
│  dotnet run flows/<flow>.cs -- <args>                          │
│    │                                                            │
│    └── existing CompositeSink(ConsoleSink, JsonlSink, …)        │
│        writes sessions/<ts>-<slug>/transcript.jsonl             │
└──────────────┬─────────────────────────────────────────────────┘
               │ Host tails the JSONL file
               ▼
       ChannelSink → SignalR → connected clients
```

The Host **does not import or modify any library agent code**. It:

- Imports `RemoteAgents` for the `AgentEvent` / `AgentQuestion` /
  `AgentResult` record types (DTOs over the wire).
- Imports `RemoteAgents` for `ProjectRegistry` to list projects.
- Spawns flows as child processes (same way `cli/bin/agents-dotnet.cs` does
  today).
- Reads `sessions/<ts>-<slug>/transcript.jsonl` to surface live events.

The Host **also** registers a `ChannelSink : IEventSink` for future v2 use,
where flows can be loaded in-process and the Host attaches the sink
directly. That path is open; v1 doesn't take it.

---

## 4. Phases

### Phase C0 — Decision capture + plan doc (½ hour)
- [x] Write this file.
- [x] Branch `phase-ui/host-mobile` off current `phase-a/local-validation` HEAD.
- [x] Commit just the plan file (in-flight Interaction-mode work stays
      uncommitted in the working tree, owned by the parallel branch).

### Phase C1 — Host scaffold + REST/SignalR surface (~1 day)

#### C1.1 — `ui/` skeleton (¼ hour)
- [ ] Create `remote-agents-dotnet/ui/` directory.
- [ ] Four empty project skeletons via `dotnet new`:
  - `RemoteAgents.Host` (`webapi`, net10.0)
  - `RemoteAgents.UI.Components` (`razorclasslib`, net10.0)
  - `RemoteAgents.UI.Web` (`blazorwasm` standalone, net10.0)
  - `RemoteAgents.UI.Maui` (`maui-blazor`, net10.0 multi-target)
- [ ] Create `remote-agents-dotnet/RemoteAgents.UI.slnx` referencing all
      four new projects + existing `src/RemoteAgents`,
      `agents/NamedAgents`, `validation/Validators`,
      `tests/RemoteAgents.Tests`.
- [ ] Confirm `dotnet build RemoteAgents.UI.slnx` exits 0 with the empty
      skeletons.

#### C1.2 — ChannelSink + FlowRunner (¼ day)
- [ ] `RemoteAgents.Host/Sinks/ChannelSink.cs` — implements `IEventSink`,
      wraps `Channel<AgentEvent>`, writer-side `EmitAsync`, reader-side
      `ReadAllAsync(CancellationToken)`.
- [ ] `RemoteAgents.Host/Runs/FlowRunner.cs` — owns the registry of active
      runs:
  - `StartAsync(project, flow, prompt, args, ct)` → spawns
    `dotnet run flows/<flow>.cs -- <args> "<prompt>"`, returns `runId`.
  - Tails `sessions/<ts>-<slug>/transcript.jsonl` line-by-line, parses each
    line back into `AgentEvent` via the existing `EventJsonContext`, writes
    to the per-run `ChannelSink`.
  - Tracks status, exit code, session dir; persists summary to `runs.json`
    on Completed/Failed.
- [ ] `RemoteAgents.Host/Runs/RunRegistry.cs` — JSON-backed list, keyed by
      `runId` (GUID); also exposes "current sink for run X" to SignalR.

#### C1.3 — Minimal REST endpoints (¼ day)
- [ ] `GET /projects` — wraps `ProjectRegistry`, returns `{ shortName, absPath }[]`.
- [ ] `GET /flows` — enumerates `flows/*.cs` minus `smoke-*`. Reads the first
      comment block of each file as a description.
- [ ] `POST /runs` — body `{ project, flow, prompt, args? }`. Calls
      `FlowRunner.StartAsync`, returns `{ runId, sessionDir }`.
- [ ] `GET /runs` — paginated list from `RunRegistry`.
- [ ] `GET /runs/{id}` — single run detail.
- [ ] `POST /runs/{id}/cancel` — flips the run's CTS.
- [ ] `GET /health` — for nssm liveness.

#### C1.4 — SignalR event hub (¼ day)
- [ ] `RemoteAgents.Host/Hubs/RunsHub.cs` — `Subscribe(runId)` server
      method; pushes events from the run's `ChannelSink` to clients via
      `Clients.Caller.SendAsync("event", AgentEvent)`.
- [ ] Polymorphic JSON setup — register `EventJsonContext` (already in
      library) with SignalR's protocol options so the `kind` discriminator
      flows through as-is.
- [ ] Smoke from `curl`/`websocat`: start a real flow, see Started →
      StreamChunk\* → Completed JSON events arrive.

**C1 gate**: hit `POST /runs` from `curl` on the laptop, see events stream
to `websocat ws://localhost:5050/runs/{id}/events`. Run shows up in
`runs.json`. **No edits to any existing file.**

### Phase C2 — Interactive-prompt seam (½ day)

The library work is already in flight on `phase-a/local-validation` (untracked
`AgentQuestion.cs`, `InteractionMode.cs`, `AgentStatus.cs`,
`PLANS/interaction-modes.md`). Once that lands on main and merges into
`phase-ui/host-mobile`, Host just consumes it.

- [ ] Surface `AgentEvent`s carrying an `AgentQuestion` over SignalR as
      `question` events with a `correlationId`.
- [ ] `POST /runs/{id}/respond` — body `{ correlationId, choice }`. Writes
      response into a per-run response channel.
- [ ] Wire response channel back into the running flow. (Mechanism depends on
      how interaction-modes.md models the resume — likely a file the flow
      polls, or a named pipe. Defer specifics until C2 starts.)
- [ ] Smoke: start a flow that triggers a tool-approval prompt under
      `InteractionMode.Interactive`; phone modal appears; choose; flow
      resumes.

**C2 gate**: round-trip prompt → response → resume works.

### Phase C3 — Tailscale binding + nssm always-on (½ day)
- [ ] Configure Host Kestrel to bind to the Tailscale interface IP only
      (read at startup from `tailscale ip -4`).
- [ ] Install Host as a Windows service via `nssm install RemoteAgentsHost`.
      Service runs as the current user (needs `~/.claude` + `~/.codex`
      auth).
- [ ] Power profile: `powercfg /change standby-timeout-ac 0`,
      `monitor-timeout-ac 30`. (Don't touch DC profile.)
- [ ] Update infra Tailscale ACL (the JSON snippet in
      [`PLANS/unity-agent-infrastructure.md`](unity-agent-infrastructure.md) §A4.2): add
      `tag:laptop:5050` → `tag:mobile`. (Edit the running tailnet config;
      no code change.)

**C3 gate**: power-cycle laptop → service auto-starts → phone hits
`http://<laptop-tailnet-ip>:5050/health` returns 200.

### Phase C4 — Shared Razor + Blazor WASM web (1 day)
- [ ] `UI.Components/`:
  - `ProjectPicker.razor` — list from `GET /projects`.
  - `FlowPicker.razor` — list from `GET /flows`.
  - `PromptForm.razor` — prompt textarea + args.
  - `RunView.razor` — live log pane subscribing to `/runs/{id}/events`
    via SignalR client; renders StreamChunks; surfaces `question` events
    as a modal with respond buttons.
  - `RunHistory.razor` — list from `GET /runs`.
- [ ] `UI.Web/` — Blazor WASM, takes `ProjectReference` on
      `UI.Components`. Routes: `/`, `/runs`, `/runs/{id}`.
- [ ] Wire Host to serve `UI.Web`'s static bundle from `/`.

**C4 gate**: open `http://<laptop-tailnet>:5050` from phone Chrome,
launch a flow, see live log, respond to a prompt.

### Phase C5 — MAUI Blazor Hybrid shells (1–2 days)
- [ ] `UI.Maui/` — `BlazorWebView` hosting the same `UI.Components`. Sets
      base API URL via build config.
- [ ] Target frameworks: `net10.0-windows10.x`, `net10.0-android`. iOS
      deferred (needs Mac).
- [ ] Native niceties:
  - Status-bar tint per run status.
  - Deep link `unityagents://run/<id>`.
  - Android notification when a `question` arrives mid-run (tap → opens
    app to that run).
- [ ] Android side-load APK to the test phone.

**C5 gate**: install APK, run a flow from the native app, notification
fires on `question` event.

### Phase C6 — Run persistence + retention (½ day)
- [ ] `~/.remote-agents/runs.json` schema versioned (`schemaVersion: 1`).
- [ ] On Host restart: orphan in-flight runs flagged `interrupted`. Phone UI
      shows them as such with a link to the session dir.
- [ ] Retention: prune `runs.json` entries older than 90 days (session dirs
      themselves stay — they're the durable record).

**C6 gate**: kill Host mid-run → restart → run appears as `interrupted`
in UI.

---

## 5. Non-goals (v1)

- **No edits to the existing library, flows, agents, validators, or CLI.**
  If a phase appears to need one, stop and resurface.
- **No multi-user.** Tailnet membership is the only auth.
- **No CRDT / collaborative editing.** Single user, single device active at
  a time per run.
- **No claude-on-VM remoting** (that's the infra-plan Track B path). The
  Host runs flows on the same machine it lives on.
- **No iOS in v1.** Defer until a Mac is available; Android-first matches
  the existing phone (`matheuss-z-fold6`).
- **No public exposure.** Tailnet only. No Caddy, no Funnel, no TLS cert
  management.

---

## 6. Risks & landmines

- **`transcript.jsonl` tail latency** — if file-system buffering delays
  flush, the Host may lag the live console output. Mitigation: explicit
  `JsonlSink` flush after each emit (already true in library — verify).
  Fallback: in-process flow loading in v2.
- **SignalR JSON polymorphism quirks** — `[JsonPolymorphic]` works in
  System.Text.Json but SignalR's hub protocol negotiation needs the
  context registered. Validate in C1.4 smoke; have MessagePack fallback
  documented if it breaks.
- **Child-process flow-spawn working dir** — flows expect to run from the
  repo root (where `projects.json` lives). Host must set
  `ProcessStartInfo.WorkingDirectory` correctly. Test with a project that
  has its own `projects.json` resolution path.
- **nssm + Tailscale startup race** — if `tailscale0` isn't up when the
  service starts, Kestrel bind fails. Mitigation: nssm dependency on
  Tailscale service, plus a retry-bind loop with backoff.
- **MAUI tooling tax** — first build on a fresh machine often pays a
  workload-install tax. Document the prereqs in `ui/README.md` before
  starting C5.
- **`AgentQuestion` response routing** — depends on how the
  interaction-modes work models flow resume (file polling? named pipe?
  stdin write?). C2 may need a tiny shim in `ui/` that bridges
  Host-received responses to whatever channel the library expects. If the
  bridge isn't possible without a library edit, C2 reopens the
  "fully additive" promise and we replan.

---

## 7. Open questions

- [ ] **Flow-to-Host response channel.** Once interaction-modes lands, how
      does the Host inject a user choice back into a running child-process
      flow? Candidates: file at well-known path, named pipe, stdin write
      via the spawned process's redirected stdin. Decision belongs to C2.
- [ ] **MAUI iOS support.** Defer to post-v1. Re-open if a Mac becomes
      available.
- [ ] **Authentication beyond tailnet.** Only relevant if a non-tailnet
      device needs access. Defer until that's a real need.
- [ ] **Run-list pagination shape.** Cursor vs page-number — defer until
      `runs.json` has enough entries for it to matter.

---

## 8. Relationship to the infra plan

The infra plan ([`unity-agent-infrastructure.md`](unity-agent-infrastructure.md))
builds a Linux VM with `ttyd + tmux + claude` for phone-accessible chat. This
plan builds a structured flow-launcher UI on the existing Windows
orchestrator. They are **complementary, not competing**:

- **Infra plan / Track A** — phone-as-terminal into a raw `claude` chat.
  Free-form conversation, no pre-defined flow.
- **This plan** — phone-as-structured-launcher into named flows
  (claude-only, claude-validate, full-review, unity-review). Same agent
  primitives, different UX.

Eventually both can coexist: tailnet has both a `ttyd` URL (chat) and a
Host URL (structured). Pick the right tool for the moment.

The infra plan is **not blocked** by this plan and vice versa. The branches
merge independently. The Host eventually ports to Linux when the C#
orchestrator does (architecture.md §11 known limit; ~1–2d Linux port when
Hetzner-VM time lands).

---

## 9. References

- Parent architecture: [`../remote-agents-dotnet/docs/architecture.md`](../remote-agents-dotnet/docs/architecture.md) §10 "UI seam (for later)".
- Parent PRD: [`csharp-orchestrator-prd.md`](csharp-orchestrator-prd.md) §3 "Secondary user (future): MAUI Blazor Hybrid".
- Parent build plan: [`csharp-orchestrator-build.md`](csharp-orchestrator-build.md) Q2 "UI direction: defer / UI-agnostic".
- Infra plan: [`unity-agent-infrastructure.md`](unity-agent-infrastructure.md) §A4.2 Tailscale ACL, A2 phone-on-tailnet validation.
- Interaction-mode work (in flight on `phase-a/local-validation`):
  `PLANS/interaction-modes.md` + `Core/Agents/{InteractionMode,AgentQuestion,AgentStatus}.cs`.
