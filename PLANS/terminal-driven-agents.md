---
type: plan
status: draft
tags: [#architecture, #ui, #agents, #pty, #terminal]
---

# Terminal-driven agents

> **Goal.** When an agent runs that owns a terminal, that terminal is a
> first-class, attach-capable thing during the run — real stdin, real
> resize, real cadence — exposed through the `Agent` contract (not the
> `PtySession`). The flow stays the protagonist; the terminal stops
> being a buffered video replay of an automation pipeline.
>
> **Product outcome.** A user opens a running flow on desktop and sees
> two co-equal live panes: a structured **conversation** (markdown
> messages + tool cards) and a real **terminal** showing the agent's
> live Claude session. Mobile shells default to conversation-only —
> equally usable. When the user wants to intervene mid-run, they click
> "Take control," type into the terminal exactly as if they were at the
> CLI (slash commands, paste, interrupt — all work), then release and
> the flow resumes its scripted driving. Fire-and-forget runs without a
> human watching behave identically to today, just on a cleaner
> transport.
>
> **Non-goal.** Long-lived sessions that outlive a flow. Session-as-a-
> service. tmux-style attach across runs. Cross-flow concurrency. See
> [Out of scope](#out-of-scope).
>
> **Ordering.** Slots after `architecture-refactor/` Phase 6, which
> introduces `IFlowExecutor` and deletes the JSONL live-tailer this
> plan also wants gone. The contracts addition (Phase T1) can land
> earlier; the transport rewrite (Phase T2) shouldn't ship until
> Phase 6 has cleared the path it would otherwise race against.

---

## Status (2026-05-29)

Plan drafted. No code yet. Depends on `architecture-refactor/` Phase 6
landing before the transport surgery (Phase 6 introduces `IFlowExecutor`
and deletes the JSONL live-tail this plan also relies on removing).
Until Phase 6 ships, T2 onward would churn.

---

## Background

### What this project is

`abox.server` is a .NET orchestrator that drives the Claude
Code CLI (and other LLM CLIs) on a Windows host to automate
Unity-project workflows. The user opens a Blazor-based UI (Web shell or
MAUI Hybrid desktop / mobile), picks a project + a flow, starts a run,
and watches it execute. Mobile/web shells reach the host over
Tailscale.

The architecture-shaping constraint: **Claude's subscription billing
requires `claude.exe` to run inside a real terminal** (`isatty()` must
be true in the child). The orchestrator satisfies this by allocating a
Windows ConPTY around `cmd.exe → claude.exe` and driving it from a
[`PtySession`](../remote-agents-dotnet/src/ABox/Core/Pty/PtySession.cs).
There is **no API-key path** — if the PTY abstraction breaks,
subscription billing breaks. This is why every option in this plan
preserves the ConPTY and never proposes "just use the streaming JSON
API." That door is closed.

### Vocabulary

- **Flow** — a scripted automation unit (`IFlow.RunAsync`). Spawns one
  or more agents in sequence, captures their results, exits. Examples
  in [`cli/flows/`](../remote-agents-dotnet/cli/flows/): `claude-only`,
  `full-review`, `unity-review`. A flow's lifetime = a single run.
- **Agent** — per-step CLI driver inside a flow. Wraps a provider
  (`ClaudeAgent`, `CodexAgent`). Owns a `PtySession`, scripts the
  launch + prompt + idle-wait + `/exit`, scrapes the final answer.
- **Session** — in this plan, the per-agent-step PTY lifetime, **not**
  a tmux-style multi-attach thing. Maps to one `claude.exe` invocation
  with a `--session-id`. Claude's own `--resume <id>` can revive
  conversation state in a *new* flow run, but no live PTY-state is
  shared across runs. This plan does not change that.
- **Host** — the ASP.NET (Kestrel) app the user opens in a browser.
  Spawns flow processes, observes their output, fans out events to UI
  shells via SignalR, persists run records.
- **Flow process** — a child of the Host. One per run. Dies when the
  flow returns. Owns the ConPTY.
- **ConPTY** — Windows pseudo-terminal API. Makes `isatty()` true in
  the child. Load-bearing for Claude's subscription auth.
- **Layer numbers** (1, 2, 4, 6, 7) — refer to
  [`architecture-refactor/`](architecture-refactor/README.md): 1 =
  Contracts (cross-boundary records), 2 = Agents (base + providers),
  4 = Flows, 6 = Host transport / lifecycle, 7 = UI client.
- **`architecture-refactor/` Phase 6** — the in-flight refactor that
  introduces `IFlowExecutor`, deletes `ClaudeJsonlTailer`'s live tail,
  collapses the second hub stream, and splits `Run` into `LiveRun` +
  `RunRecord`. This plan extends Phase 6's surface with the capability
  interface and live byte channel.

### Current architecture (today)

Three processes are in play during a live run:

```
┌────────────────────────────────┐   ┌────────────────────────────────┐   ┌────────────────────────┐
│ Host (ASP.NET, Kestrel)        │   │ Flow process (child of Host)   │   │ Browser / MAUI shell   │
│ - Spawns flow process per run  │   │ - Runs IFlow.RunAsync          │   │ - Blazor UI            │
│ - Tails transcript.jsonl       │←──│ - Owns ConPTY for claude.exe   │   │ - xterm.js (no stdin)  │
│ - SignalR fan-out              │   │ - Writes transcript.jsonl      │   │ - Chat cards           │
│ - Persists RunRecords          │   │ - Exits when flow returns      │   │                        │
└──────────────┬─────────────────┘   └────────────────────────────────┘   └────────────┬───────────┘
               │                                                                       │
               └─────────────────── SignalR over WebSocket ────────────────────────────┘
```

Today's byte path for "live terminal":

```
claude.exe TUI output
  → ConPTY (in flow process; hardcoded 120×40)
  → PtySession.ReadLoopAsync (UTF-8 decode, 4 KB chunks)
  → AgentEvent.StreamChunk wrapped by JsonlSink
  → transcript.jsonl on disk (one JSON-escaped line per chunk)
  → Host file-tail (FileStream + 50 ms poll loop)
  → JSON deserialize → ChannelSink → Broadcaster
  → SignalR to browser
  → xterm.js write (disableStdin: true, no resize feedback)
```

Parallel "chat cards" path:

```
claude.exe writes ~/.claude/projects/<encoded>/<session-id>.jsonl
  → Host ClaudeJsonlTailer (150 ms poll)
  → parses user/assistant/thinking/tool_use/tool_result
  → ChatEvent stream → SignalR → Razor cards
```

Both paths render the same conversation in different shapes with
different latencies. They were added in different refactor rounds and
no longer have a clear hierarchy.

### What we tried before

The "live terminal" pane in the UI has been refactored at least four
times. Each attempt stayed inside the same byte path and treated the
issue as a *rendering* problem. The history (for the cold reader who
didn't live through it):

1. **Regex-strip ANSI + render as `<pre>`** (early). `RunView.Sanitize`
   stripped escapes and folded `\r` into `\n`. Lost cursor positioning,
   alt-screen, color. Produced the "Inieni\nnnng" spinner mess —
   animation frames that were supposed to overwrite in place became
   garbled stacked text. Documented in the comment at
   [xterm-viewer.js:11](../ui/ABox.UI.Components/wwwroot/js/xterm-viewer.js#L11).
2. **xterm.js retrofit** (commit `88ee4b8`). Replaced `Sanitize` with a
   real VT100 emulator. Escape sequences now parse correctly. Removed
   "weird symbols" most of the time but symptoms persisted: animations
   arrive bunched, late-join clients miss the alt-screen handshake,
   grid size diverges from the underlying PTY. Still no stdin.
3. **Structured chat view from Claude's session JSONL** (commit
   `5f5c1f2`, "C7"). Added a second tailer reading
   `~/.claude/.../session.jsonl` to render a clean conversation view
   (markdown messages, tool cards). Solved readability but introduced
   a competing rendering of the same conversation — two views, two
   latencies, neither canonical.
4. **Replay broadcaster + paired tool rows** (also commit `88ee4b8`).
   Added a ring buffer on the Host so late-joining browsers get recent
   events on attach. The buffer is bounded; long runs still lose the
   alt-screen opening escape.

Each round narrowed the symptom but the underlying byte path didn't
change: PTY bytes are JSON-encoded into a file the Host polls. That
path structurally cannot deliver:

- The **cadence** of a live terminal (file poll + JSON round-trip
  introduce coalescing latency).
- **Stdin** (no path exists from browser back to PTY).
- **Resize** (no SIGWINCH back-channel; PTY is hardcoded 120×40).
- **Alt-screen on late join** (a bounded replay buffer can't reach
  back to an arbitrarily old escape sequence).

This plan changes the byte path and the agent contract, not the
renderer.

---

## Root causes (what the prior attempts couldn't fix)

The "weird symbols + doesn't feel like a real terminal" complaint
decomposes into five specific issues, none of which can be solved
inside the renderer:

1. **No stdin.** [xterm-viewer.js:26](../ui/ABox.UI.Components/wwwroot/js/xterm-viewer.js#L26)
   sets `disableStdin: true`. The "terminal" is a silent replay; slash
   commands, paste, interrupt all impossible.
2. **No resize back-channel.** ConPTY opened at hardcoded 120×40 in
   [ClaudeAgent.cs:22-23](../remote-agents-dotnet/src/ABox/Providers/Claude/ClaudeAgent.cs#L22).
   Browser xterm.js fits to viewport. The two diverge, cursor escapes
   land at wrong cells, repaints don't overlay.
3. **Buffered transport breaks animation cadence.** Bytes go
   `PtySession → AgentEvent.StreamChunk → JsonlSink → transcript.jsonl
   → Host 50ms file-tail → SignalR → xterm.js`
   ([FlowRunner.cs:242-272](../ui/ABox.Host/Runs/FlowRunner.cs#L242)).
   Spinners and progress bars arrive bunched. The cursor math is fine;
   the *cadence* is wrong.
4. **Late-join misses the alt-screen handshake.** Claude emits
   `\e[?1049h` once at run start. A browser that connects late and
   misses that single escape sees subsequent repaints scroll instead of
   overwrite — the "weird symbols accumulating" complaint.
5. **Two parallel renderings of one conversation disagree.** The C7
   commit added structured chat rendering from `~/.claude/.../session
   .jsonl` (RunView.razor:60-124). It runs alongside the PTY replay and
   is sometimes ahead, sometimes behind. Neither feels canonical.

Each is downstream of one design decision: **the PTY is owned by an
ephemeral flow process and its bytes are routed through a JSONL file
that the Host polls.** No amount of polish on top of that path fixes
the symptoms.

---

## Principles

1. **The Agent owns the terminal.** Nothing outside the agent talks to
   the `PtySession`. Input, resize, and observation flow through the
   agent's public surface. The terminal stops being a leaky
   implementation detail.
2. **Capability over presence.** Some agents have terminals
   (`ClaudeAgent`), some won't (future library-mode or HTTP agents).
   Capability is declared by an interface, not by base-class behavior.
   Modeled on VS Code's `Terminal` API — UI talks to the agent, not the
   pty.
3. **Flow stays the driver.** The orchestrator-as-protagonist model
   from `architecture-refactor/` is unchanged. The agent's terminal is
   live-attachable *during* the agent step; when the step ends, the
   terminal ends. The chat projection (from the session JSONL Claude
   writes itself) is the historical record.
4. **Driver-conflict policy lives on the agent.** Not on the Host, not
   on the UI. The agent knows what phase it's in (scripted vs idle)
   and decides what to do with user input.
5. **One live byte channel; one archival channel.** Live bytes flow
   agent → Host → SignalR. `transcript.jsonl` stays as archival/replay
   only — it stops being on the hot path.
6. **No invented transport.** SignalR is already wired and is the .NET
   idiom for the WebSocket-based PTY relay that ttyd / wetty /
   code-server / VS Code Remote all use. Keep it.

---

## Target shape

### Layer 1 — Contracts (extension)

Add a capability interface alongside `IAgent`:

```csharp
// New in ABox.Contracts
public interface ITerminalCapable
{
    // Terminal byte stream is already exposed via AgentEvent.StreamChunk —
    // no new event variant needed.

    Task SendInputAsync(string text, CancellationToken ct);
    Task ResizeAsync(int cols, int rows, CancellationToken ct);
}

public enum TerminalInputPolicy
{
    Rejected,   // agent is mid-scripted-phase; input dropped (with optional event)
    Queued,     // input held until next idle; then forwarded
    Forwarded,  // input went straight to PTY
}

public sealed record TerminalInputResult(TerminalInputPolicy Policy, string? Reason = null);
```

`SendInputAsync` returns `TerminalInputResult` so the caller (Host →
UI) can render "your input was queued" / "rejected: agent is mid-prompt
submission" cues without guessing.

### Layer 2 — Agents

`ClaudeAgent` implements `ITerminalCapable`. The `PtySession` stays
private. The two new methods delegate:

```csharp
public Task SendInputAsync(string text, CancellationToken ct)
    => _inputGate.RouteAsync(text, _session, _phase, ct);

public Task ResizeAsync(int cols, int rows, CancellationToken ct)
    => _session.ResizeAsync(cols, rows, ct); // PtySession gains Resize
```

`_phase` is the agent's scripted-phase tracker (LaunchSettle,
DialogDismiss, Submitting, AwaitingReply, Exiting). `_inputGate` is
the policy machine — observe-only by default, "take control" pauses
scripted submission at the next idle boundary and flips the gate to
`Forwarded`.

`PtySession` grows one method:
```csharp
public Task ResizeAsync(int cols, int rows, CancellationToken ct)
    => Task.Run(() => _pty.Resize(cols, rows), ct); // Porta.Pty supports this
```

### Layer 4 — Flows

No flow-body changes. Flows still call `await agent.ExecuteAsync(req,
ct)`. The capability surface is consumed by the *Host*, not by flow
code.

### Layer 6 — Host

The flow process publishes its agent's terminal events to the Host via
a **live channel**, not by writing to `transcript.jsonl` and having
Host re-read. Concrete options (decision deferred to Phase T2 — see
[Transport mechanism](#transport-mechanism)):

- **Named pipe** per run (Windows-native, in-band with the flow
  process; small new dependency).
- **localhost loopback HTTP/SignalR client from the flow** (no new
  IPC primitive; reuses what the Host already speaks).
- **stdout framing** (cheapest; abuses the existing
  RedirectStandardOutput path that today carries one line — the
  session-id sniff at [FlowRunner.cs:200](../ui/ABox.Host/Runs/FlowRunner.cs#L200)).

The Host's `RunsHub` gains:
- `SendInput(runId, text)` → routes to the live agent's `SendInputAsync`.
- `Resize(runId, cols, rows)` → routes to the live agent's `ResizeAsync`.
- Per-run ring buffer of recent terminal bytes (size: enough to capture
  the alt-screen handshake + a few seconds of repaints; tunable).
  Late-attach clients receive the buffer once, then live bytes.

`transcript.jsonl` is **no longer tailed for live events**. It becomes
the post-run archival record (what `RunHistory` reads). Phase 6 of
`architecture-refactor/` was already going to delete the live tail —
this plan continues that deletion through to the rendering path.

### Layer 7 — UI

`XtermViewer` becomes a real attached terminal:

- `disableStdin: false`.
- `onData` handler in `xterm-viewer.js` → JS interop call into
  Razor → `RunsHub.SendInput(runId, data)`.
- `onResize` → `RunsHub.Resize(runId, cols, rows)`.
- FitAddon drives the size; xterm is no longer cosmetically wrong-sized
  against a fixed-grid PTY.

`RunView.razor`:
- Remove the "Live terminal" `<details>` toggle (line 147) — the
  terminal becomes a peer pane next to "Conversation" while the run is
  active.
- Add a **Driver indicator + Take Control / Release** button. States:
  *Flow driving* (observe-only), *You are driving* (input forwarded),
  *Releasing…* (waiting for next idle to hand back).
- Chat view (from `ChatEvent`) stays as-is — it's the readable
  projection that survives the run.

`ChatEvent` continues to come from Claude's own session JSONL
([ClaudeJsonlTailer.cs](../ui/ABox.Host/Runs/ClaudeJsonlTailer.cs)),
which `architecture-refactor/` Phase 6 plans to delete in favor of
agent-emitted `AgentEvent` variants. **This plan does not block on
that fold** — terminal-driven rendering works equally well with the
current ChatEvent stream or with the future folded one.

---

## Current structure (citations)

- PTY ownership / scripted phases: [ClaudeAgent.cs:84-139](../remote-agents-dotnet/src/ABox/Providers/Claude/ClaudeAgent.cs#L84)
- PTY size hardcoded: [ClaudeAgent.cs:22-23](../remote-agents-dotnet/src/ABox/Providers/Claude/ClaudeAgent.cs#L22),
  [ClaudeAgent.cs:186-195](../remote-agents-dotnet/src/ABox/Providers/Claude/ClaudeAgent.cs#L186)
- PTY plumbing: [PtySession.cs:39-180](../remote-agents-dotnet/src/ABox/Core/Pty/PtySession.cs#L39)
- Live bytes routed through JSONL file: [FlowRunner.cs:219-272](../ui/ABox.Host/Runs/FlowRunner.cs#L219)
- xterm.js no-stdin: [xterm-viewer.js:22-34](../ui/ABox.UI.Components/wwwroot/js/xterm-viewer.js#L22)
- RunView terminal pane: [RunView.razor:147-150](../ui/ABox.UI.Components/Pages/RunView.razor#L147)
- StreamChunk consumption: [RunView.razor:233-235](../ui/ABox.UI.Components/Pages/RunView.razor#L233)
- Session-id sniff over stdout: [FlowRunner.cs:200](../ui/ABox.Host/Runs/FlowRunner.cs#L200)

---

## Gap

1. No agent-level contract for terminal input/resize. UI has no way to
   reach the PTY without breaking encapsulation; today it *doesn't*
   reach it — there is no input path at all.
2. PTY size is fixed in code; no resize support on `PtySession`.
3. Live bytes ride the archival JSONL file path. 50ms poll + JSON
   round-trip + line-buffering coalesces animations.
4. No ring buffer for replay; late attach loses alt-screen.
5. No driver-conflict model. The codebase has no notion of "who is
   currently typing into the PTY."
6. `RunView` treats the terminal as a debug toggle below the chat
   instead of a co-equal live view of the current agent.

---

## Migration phases

### Phase T1 — Capability contract (Layer 1)

Add `ITerminalCapable`, `TerminalInputPolicy`, `TerminalInputResult` to
`ABox.Contracts`. No implementations yet.

`PtySession.ResizeAsync` lands here as well (no callers yet).

**Exit criterion:** Contracts assembly builds. Nothing else changes
behaviorally.

**Can ship independently.** No dependency on architecture-refactor
Phase 6.

### Phase T2 — Live byte channel (Layer 6)

Decide and implement the live channel between flow process and Host.
This is the work item that **must wait for `architecture-refactor/`
Phase 6** because Phase 6 is replacing `FlowRunner`/`IFlowExecutor`
and the live channel needs to live in the executor that wins.

Sub-decisions (this phase opens with):
- T2a. Pick transport: named pipe / loopback SignalR / stdout framing.
  Default recommendation: **stdout framing of a typed line protocol**
  (cheapest, no new IPC, sits next to the existing session-id sniff).
  If stdout proves too lossy under load, switch to named pipe.
- T2b. Per-run ring buffer in the Host. Size target: 64 KB (covers
  alt-screen + first paint comfortably).
- T2c. Stop the `transcript.jsonl` *live* tail. Keep the archival
  write. `RunHistory` reads the archival file post-run, same as today.

**Exit criterion:** A running flow's terminal bytes arrive in the
browser without going through a file. `ClaudeJsonlTailer`'s
*live* role is gone (its archival role can move into Phase 6's
post-run reader).

### Phase T3 — Capability implementation (Layer 2)

`ClaudeAgent : ITerminalCapable`. `SendInputAsync` and `ResizeAsync`
land. Driver-policy gate is implemented with **observe-only default,
explicit take-control**.

Scripted-phase tracker:
- `LaunchSettle` — input rejected (claude not ready).
- `DialogDismiss` — input rejected (we're handling a modal).
- `Submitting` — input queued (we're typing the prompt; user input
  appended after the `\r`).
- `AwaitingReply` — input rejected unless take-control is held.
- `TakeControlHeld` — input forwarded.
- `Exiting` — input rejected.

Each transition fires an `AgentEvent.DriverPhaseChanged` so the UI can
update the indicator without polling.

**Exit criterion:** `ClaudeAgent` implements `ITerminalCapable`. Driver
policy is testable in isolation (unit test against a
`FakePtyConnection`).

### Phase T4 — UI attach (Layer 7)

`XtermViewer` enables stdin and resize. `RunsHub` exposes `SendInput`
and `Resize`. `RunView` reorganizes:

- "Conversation" (chat cards) and "Terminal" (xterm) become side-by-
  side panes during an active run (or stacked on narrow screens).
- "Take control" / "Release" button, with the current driver shown.
- Mobile shells default to chat view only — terminal pane hidden,
  toggleable.

**Exit criterion:** A user can attach to a running flow, click "Take
control," type a slash command into Claude, see it execute, release,
and the flow resumes. Mobile shell shows chat only by default.

### Phase T5 — Cleanup

Delete the `transcript.jsonl` live-tail code path (Phase 6 of the
architecture refactor was already going to do this; this phase
confirms it happens and the buffer ring replaced it).

Delete `RunView.Sanitize` / `LineKind.Stream` if not already gone
from Phase 8 of architecture-refactor.

**Exit criterion:** `Host/Runs/ClaudeJsonlTailer.cs` deleted *or* its
remaining role is unambiguously archival-only (post-run, single read).
No file-tail on the live render path.

---

## Acceptance criteria

The plan is done when:

1. A user opens a running flow on desktop, sees the terminal pane
   live, clicks "Take control," types `/help` into Claude, sees the
   slash menu render, clicks "Release," and the flow's scripted exit
   completes normally.
2. Resizing the browser window resizes the underlying PTY (verified by
   `tput cols` echoing the new value inside a take-control session).
3. A user opens the page mid-run, **after** Claude's startup paint,
   and sees the current TUI state correctly composed (alt-screen
   semantics preserved by the ring buffer hydrate).
4. The byte path is one hop: flow agent → Host → SignalR → xterm.
   No file polling in the live path. `ClaudeJsonlTailer`'s live tail
   gone.
5. Mobile shell defaults to chat-only and is fully usable that way;
   terminal pane is opt-in.
6. Driver indicator accurately reflects scripted phase transitions
   (verified by visible state changes during a smoke flow).

---

## Driver-conflict policy (v1)

**Observe-only by default. Explicit take-control.**

States visible to UI:
- *Flow driving (observe-only)* — `SendInputAsync` returns
  `Rejected`.
- *Take control requested* — user clicked the button; agent's input
  gate is set to "forward at next idle." Indicator shows "Waiting for
  flow to pause…"
- *You are driving* — input forwarded. Flow's `WaitIdleAsync` is held
  (it sees activity from the user and keeps waiting); flow does NOT
  proceed to `/exit` until released.
- *Releasing…* — user clicked release; flow resumes from its next
  scripted step.

Rejected variants (see [Out of scope](#out-of-scope)): lock-based
contention, multi-driver merge, simultaneous human+script writes.

---

## Transport mechanism

Three plausible transports for live PTY bytes from the flow process to
the Host. Recommendation: **stdout framing of a typed line protocol**.

| Option | Pros | Cons |
|---|---|---|
| **Stdout framing** | No new IPC; reuses existing pipe; flow process already redirects stdout. | Stdout is a shared stream; needs framing discipline (one JSON event per line, with a sentinel). |
| **Named pipe per run** | Native Windows IPC; clean separation from stdout; backpressure-friendly. | Adds a new IPC primitive; lifecycle tied to flow process. |
| **Loopback SignalR client from flow** | Reuses an already-spoken protocol; symmetric with browser. | Adds a SignalR client dep to the flow process; weird for a child process to talk back to its parent over the same bus the parent uses for browsers. |

If T2 finds stdout too lossy under burst (it's a 4KB pipe; sustained
animation frames could backpressure the agent), switch to named pipe.
Decision recorded here, revisitable.

---

## Relationship to other plans

- **`architecture-refactor/` Phase 6** — deletes
  `ClaudeJsonlTailer`'s live tail, splits `Run` → `LiveRun + RunRecord`.
  This plan extends Phase 6 with the capability surface and ring
  buffer. T2 ships **after** Phase 6's `IFlowExecutor` implementations
  exist.
- **`architecture-refactor/` Phase 7 (events fold)** — folds
  `ChatEvent` into `AgentEvent`. This plan does not depend on the fold;
  it works with either the current dual stream or the folded one.
- **`architecture-refactor/` Phase 8 (UI cleanup)** — deletes
  `RunView.Sanitize` and the mirror records. Cosmetic overlap with T4
  but independent.
- **`interaction-modes.md`** — the "agent needs input" modal in
  [RunView.razor:42-58](../ui/ABox.UI.Components/Pages/RunView.razor#L42)
  is a natural place to surface the **take-control** prompt: instead of
  Yes/No, offer "take control of the terminal to answer." Out of scope
  for v1 but the integration point is named here so the two plans
  don't collide.
- **`unity-agent-infrastructure.md`** — independent. This plan changes
  the local UI ergonomics; the Hetzner VM track sees no difference
  because everything runs the same way over Tailscale once the
  transport is one-hop.

---

## Out of scope

These are out of scope for v1. Listed so they're not mistaken for
forgotten:

- **Long-lived sessions across flows.** No "resume the conversation"
  via UI — that's still the `--resume <session-id>` story at flow
  invocation, same as today.
- **Multiple browsers driving at once.** Single driver per run.
  Observer browsers see the same byte stream read-only.
- **Lock-based driver contention.** Single-driver-with-takeover is
  the only policy v1 supports. If multi-driver becomes a real need,
  it's a v2 question.
- **Cross-flow PTY pooling.** Each agent step gets its own ConPTY.
  No reuse.
- **Replacing SignalR.** SignalR is the right tool; the cleanup
  happens upstream of it (replacing the file-tail with a direct
  channel), not at the SignalR layer.
- **Non-terminal agents.** Future agents that don't implement
  `ITerminalCapable` are *supported* by this plan (they simply don't
  get a terminal pane) but no such agent is built here.
- **Recording / replaying past runs as live terminals.** The chat
  view is the post-hoc record. The terminal is live-only.

---

## Open questions

1. **Resize policy when multiple observers attach with different
   viewports.** First-attach sets the size? Smallest grid wins?
   Take-control owner sets? — defer to T4; spike with first-attach
   wins and revisit if it feels wrong.
2. **Ring buffer size.** 64 KB is a guess based on Claude's startup
   paint. Measure during T2 and tune.
3. **Naming.** `ITerminalCapable` vs `IInteractiveAgent` vs
   `ITerminalHost` — bikeshed during T1 review.
4. **Where does take-control gate state live for restart-survival?**
   If the Host restarts mid-run, does take-control persist? v1: no,
   restart drops to observe-only.
