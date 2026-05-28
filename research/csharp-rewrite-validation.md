# C# rewrite validation + UI architecture notes

Date: 2026-05-28
Status: validation complete — green light on technical risk; awaiting user decision on rewrite

Pairs with [`pty-smoke-result.md`](./pty-smoke-result.md) (the original Node/JS PTY validation).

---

## Why this exists

The JS orchestrator at `remote-agents/orchestrator/` works, but lacks structural enforcement:

- Providers (`claudeProvider.js`, `codexProvider.js`) are duck-typed — nothing makes their interfaces stay aligned as we add more agents (cursor, etc.).
- Event emission is by convention, not enforced. A subclass could skip logging entirely.
- No compile-time refactoring, no exhaustive switch on event types in a future replay viewer.

User wants to rewrite in **C#** for:
- Abstract `Agent` base class with **sealed** `RunAsync` that owns logging/timing/event emission
- Compile-time enforced provider contract
- Discriminated `AgentEvent` hierarchy (sealed records)
- Real refactoring + autocomplete + types

**The load-bearing risk:** can .NET drive Claude's TUI through ConPTY reliably, the way `node-pty` does today? Everything else is conventional .NET work.

---

## What we built to validate

Standalone smoke test outside the repo (zero touch to existing orchestrator):

```
C:\Unity\dotnet-pty-smoke\
├── PtySmoke.csproj          .NET 10 console app
├── Program.cs               3-stage smoke
├── pty-stage1.log           `claude --version` PTY timeline
├── pty-stage2.log           Claude TUI session timeline — great reference for ANSI/cursor behavior
├── pty-stage3-auth.log      `claude auth status` JSON post-test
└── stage2-work/             empty workdir for the test claude session
```

### Dependencies

- **`Microsoft.Windows.Console.ConPTY`** 1.24 (owner: `Microsoft.Terminal`) — first-party, ships native `conpty.dll`. No managed API.
- **`Porta.Pty`** 1.0.7 — managed wrapper that P/Invokes `CreatePseudoConsole`/`ResizePseudoConsole`/`ClosePseudoConsole` via `Vanara.PInvoke.Kernel32`. 34K downloads. Cross-platform (Windows/Linux/macOS).

### Test stages

1. **Sanity:** spawn `cmd.exe` via Porta.Pty, run `claude --version\r`, capture, `exit\r`. Validates ConPTY + claude.exe basics.
2. **TUI:** spawn `cmd.exe`, run `claude --permission-mode acceptEdits --session-id <our-uuid> "Reply with exactly the word PONG"`, dwell, handle trust dialog, idle-wait, send `/exit`, send `exit`. Validates real workload path.
3. **Auth:** shell out (no PTY) to `claude auth status`, assert subscription-path output. Validates no billing-mode contamination.

---

## Results

| Stage | Result | Evidence |
|---|---|---|
| 1. `claude --version` via ConPTY | **PASS** | exit 0, version regex matched |
| 2. Claude TUI via ConPTY | **PASS** (after fix — see below) | 6072 chars captured, `PONG` round-tripped in stream, session-id JSONL written under `~/.claude/projects/...`, clean exit code 0 |
| 3. Auth status post-test | **PASS** | `claude.ai` / `Max` markers present in output |

**Total runtime: 18.2s** for all 3 stages. Stage 2 alone ~10s — within noise of JS orchestrator's claudeProvider (~12–15s).

### Confirmed

- ConPTY drives Claude's TUI start-to-finish (alt-screen, cursor moves, repaints, color, mouse-tracking escapes)
- `--session-id <uuid>` passthrough works — claude wrote `~/.claude/projects/.../{our-uuid}.jsonl`
- Resume hint appears in stream on clean exit: `Resume this session with: claude --resume <uuid>`
- Subscription billing path unaffected — `auth status` post-test still shows `authMethod:claude.ai`
- Existing JS orchestrator at `C:\Unity\remote-unity-agents\` was not touched

---

## Bugs found (carry forward into rewrite OR backport to JS)

1. **Trust-dialog text changed in Claude v2.1.x.**
   - First-run smoke had: matching on `"Do you trust"` / `"Trust this folder"`
   - Actual claude wording: `"Is this a project you created or one you trust?"` + button `"Yes, I trust this folder"`
   - Fix: match on `"trust this folder"` / `"one you trust"` (case-insensitive)
   - **Action item:** check whether `remote-agents/orchestrator/src/providers/claudeProvider.js` `detectStartupDialog` has the same gap. Worth grep + port even if we proceed with the C# rewrite.

2. **`Porta.Pty.IPtyConnection.ExitCode` throws if process is alive.**
   - Caller's `Process.ExitCode` getter throws `InvalidOperationException: Process must exit before requested information can be determined.` if `HasExited == false`. Porta.Pty doesn't guard this.
   - Fix in the eventual C# `Agent` base class: expose `int? ExitCodeOrNull` that does the `HasExited` check itself.

3. **Microsoft's first-party ConPTY NuGet package is native-dll-only.**
   - `Microsoft.Windows.Console.ConPTY` ships `conpty.dll` for redist but has no managed API surface — it's purely a binary drop.
   - Microsoft does not publish a managed PTY wrapper on NuGet. (The `microsoft/vs-pty.net` GitHub repo is not on NuGet under that name; searching for `Pty.Net` returns "no versions available.")
   - We landed on community wrapper `Porta.Pty`. Worked on first integration; no edge cases hit.

---

## Bottom line on C# viability

**Green-lit on the load-bearing risk.** No platform surprises. The rewrite is conventional .NET work from here.

| Question | Answer |
|---|---|
| Can C#/.NET drive ConPTY reliably? | Yes |
| Does it work with Claude's TUI specifically? | Yes |
| Does `--session-id <uuid>` passthrough work? | Yes |
| Does PTY-driven claude still use subscription billing? | Yes |
| Is a published managed PTY library available? | Yes — Porta.Pty |
| Performance comparable to node-pty? | Yes — within noise |

---

## UI architecture discussion (no code yet)

After PTY validation passed, conversation moved to UI direction.

### User's target fleet
- Web app
- Mobile app (Android) — clarified as **app-like, NOT necessarily Kotlin-native**
- Possibly Windows desktop app

User explicitly does not need platform-native code (no Kotlin/Swift requirement). React Native, Blazor, Flutter, or Tauri all acceptable. Prefers "real app" over PWA for the more-native feel.

### The three viable one-codebase stacks

| Stack | Language | Web | Android | Windows | Backend-pair |
|---|---|---|---|---|---|
| **Tauri 2** + React/Svelte | TS frontend, Rust shell | Same web app | Real APK (Tauri Mobile, 2024+) | Real .exe, tiny binary | Pairs with TS orchestrator for type sharing |
| **MAUI Blazor Hybrid** | C# end-to-end | Blazor Server/WASM | Real APK (native WebView + embedded .NET) | Real .exe (native WebView + embedded .NET) | Pairs with C# orchestrator for type sharing |
| **Flutter** | Dart | Yes (JS/Wasm) | Native-feeling APK | Real .exe | Backend-agnostic (codegen client from OpenAPI) |

### Coupling between orchestrator language and UI choice

The orchestrator and UI talk over HTTP+WebSocket, so the languages are *technically* decoupled — but for **type sharing without codegen**, they need to match:

- C# orchestrator ↔ MAUI Blazor: shared types via direct project reference
- TS orchestrator ↔ Tauri+React: shared types via npm workspace
- Flutter UI: backend-language doesn't matter, always codegen client from OpenAPI

### Direction emerging (not committed)

Strongest fit given user's stated priorities (types, classes, discipline, app-like UI, hobby pacing):

**Option A — preferred:** C# orchestrator + **MAUI Blazor Hybrid UI**
- One language top to bottom
- Same Razor components render as web page, Windows window, Android screen
- Direct type sharing — no OpenAPI codegen step
- Native WebView on desktop/mobile means no Blazor WASM cold-start penalty
- Trade-off: MAUI Android feel is good but not Flutter-good; smaller UI ecosystem than React; Blazor learning curve

**Option B — fallback if Blazor or MAUI mobile feel is a concern:** C# orchestrator + **Tauri 2 + React** UI
- Plain TS+React for web; same codebase wrapped by Tauri for Windows + Android APK
- More polished UI ecosystem than Blazor
- OpenAPI codegen for TS types (one automated build step)
- Tauri Mobile is newer / less battle-tested than the desktop story

**Not recommended:** TS orchestrator. The web-share-types argument only holds if UI is also plain TS, and the user wants an "app" stack (Tauri/Blazor/Flutter) — which gives the UI its own codebase shape regardless. The remaining differentiator is C#'s compile-time discipline, which the user has consistently said they want.

---

## State at handover

- `remote-unity-agents` repo: **unchanged** (clean working tree on `phase-a/local-validation`, all earlier work pushed)
- New folder `C:\Unity\dotnet-pty-smoke\` exists outside the repo — can be deleted any time without consequence
- `pty-stage2.log` is the most useful artifact — it shows exactly what Claude's TUI byte stream looks like when driven via ConPTY (alt-screen entry, cursor positioning, color codes, trust dialog, status indicators, response stream, exit sequence)

## Open decisions

1. **Commit to C# rewrite, or apply lessons back to JS and defer?**
   - JS-backport path: port `detectStartupDialog` trust-dialog regex fix; add JSDoc-typed abstract `Agent` class hierarchy; emit discriminated event union via `@typedef`. Cheaper, no compile-time guarantee.
   - C# rewrite path: see scope below.
2. **If rewrite: UI direction (A or B above)?** This affects whether the orchestrator needs OpenAPI codegen tooling from day one.
3. **Repo layout if rewrite:** new `remote-agents-dotnet/` alongside existing JS? Rename JS to `legacy/`? Single repo or split?
4. **JS orchestrator backport regardless of rewrite decision:** apply the trust-dialog regex fix (`"trust this folder"` / `"one you trust"`) to `claudeProvider.js` — small change, prevents a real bug whether or not we rewrite.

## Suggested next session opening

> "Continuing from `csharp-rewrite-validation.md`. PTY-via-ConPTY in C# is proven working. Pending decisions are documented in that file's 'Open decisions' section. I'd like to [pick A / pick B / defer rewrite / port the trust-dialog fix to JS first / scope the C# project layout]."
