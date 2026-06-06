# L9 validation handoff — run the live terminal gate (Windows or Linux)

This is a self-contained brief for a **local agent on a machine with a real
`claude` + subscription** (Windows or Linux — the PTY layer is confirmed
cross-platform). The container this was built in has no subscription, so L9's
substrate is unit-green but its **live "done-when" is unverified.** Your job is to
verify it on real metal and report back.

**Already validated on Linux (no subscription):** PTY spawn, the bash/cmd→`claude`
launch, and claude rendering its TUI in the PTY all work — a probe drove real
`claude 2.1.x` to its first-run screen. What remains unverified is everything
*behind the startup dialogs*: trust → login → prompt-ready → submit → response →
JSONL resolve. That is what you're closing.

## What you're validating

L9 (`PLANS/rebuild/03-implementation-plan.md` § L9) is the real terminal substrate:
`PtySession` (ConPTY via Porta.Pty) + the `ClaudeProvider`/`CodexProvider` driving
real CLIs. The gate to close it:

1. **A real `claude` PONG round-trips end-to-end** through the full stack (FlowLauncher → Flow.Run → ClaudeProvider → PTY → JSONL resolve → snapshot).
2. **Subscription path intact** — Oracle Tier A1/A2/A3: the child must run on the Max subscription, and subscription/API keys must be scrubbed from the child env (`SubscriptionGuard` + `EnvScrub`). A leaked key must *refuse to start*.
3. **Anti-zombie teardown** (Oracle A10) — no orphaned `claude`/`cmd.exe` processes survive a run, including a hung/cancelled run.
4. **Latency within the prototype envelope** — a ping should settle in roughly the same wall-clock the prototype took (tens of seconds, not minutes).

## Preconditions

- Windows 10/11 **or** Linux/macOS.
- **.NET 10 SDK** (`dotnet --version`).
- **`claude` CLI** installed and on `PATH`, signed in to a **Max subscription** (required).
- *(Optional, for the Codex gate)* **`codex` CLI** on `PATH`, signed in to ChatGPT.
- **Configure the CLI once, interactively, before running the gate.** On a *fresh*
  profile claude shows first-run dialogs the orchestrator does **not** auto-dismiss
  — notably a **theme picker** ("Choose the text style…"). `DetectStartupDialog`
  only handles the **Trust-folder** and **Bypass-permissions** prompts (Oracle A7),
  so run `claude` by hand once in the target project dir to pick a theme, trust the
  folder, and log in. After that the orchestrator drives a configured CLI straight
  to the prompt. (This is why ephemeral containers need claude pre-seeded in setup,
  not handled in code.)

## Steps

1. **Get the code** (latest `main` has the merged refactor + L9 substrate):
   ```
   git pull origin main
   dotnet build RemoteAgents.slnx     # expect: warning-free
   ```

2. **Un-skip the live smoke tests.** They are `[Fact(Skip = "integration: …")]` by
   design. Temporarily remove the `Skip` argument (don't commit that) on:
   - `tests/RemoteAgents.Tests/ClaudeSmokeTests.cs` → `FlowLauncher_drives_the_registered_claude_flow_end_to_end`
   - *(optional)* `tests/RemoteAgents.Tests/CodexSmokeTests.cs`

   These already assert the **end-to-end** gate (#1): `Phase == Completed`,
   `op.Name == "ping"`, `op.Status == Completed`, and the summary **contains
   `PONG`**.

3. **Run just those tests** and watch the output:
   ```
   dotnet test RemoteAgents.slnx --filter "FullyQualifiedName~SmokeTests"
   ```
   The test prints `Phase=… Op=… Status=…` and `Summary=…` via `ITestOutputHelper`.

4. **Subscription scrub (#2) — verify the refusal path.** With a subscription key
   set in your shell (e.g. `ANTHROPIC_API_KEY`), confirm `SubscriptionGuard`
   refuses to start rather than leaking it to the child, and that the child env is
   scrubbed (`EnvScrub.SubscriptionKeys`). A passing happy-path run already proves
   the scrub doesn't break normal operation; this step proves it *fails closed*.

5. **Anti-zombie (#3) — manual.** There is no automated zombie test. During/after a
   run, in a second terminal:
   ```
   Get-Process claude, cmd -ErrorAction SilentlyContinue
   ```
   After the run completes (and after a **cancelled** run — cancel mid-flight),
   confirm no stray `claude`/`cmd.exe` children linger. The kill-on-hang guarantee
   lives in `PtySession` (Oracle A10: `ct.Register(KillQuietly)` + `ShutdownAsync`).

6. **Latency (#4).** Note the wall-clock from `Start` to `Completed` (the test has a
   3-minute timeout; a healthy ping is far under that).

## Report back

- Pass/fail for each of #1–#4, with the printed `Phase/Op/Status/Summary` line.
- Any timing that felt outside the prototype envelope (and the rough number).
- Any place the choreography (startup dialogs, prompt-ready detection, `/exit`→`exit`
  teardown) needed nudging — those are the Tier B1 timings that may need re-tuning.
- **Do not commit** the un-skip change. If you find a real bug, capture it as a note
  (or a separate fix PR) rather than editing the gate to make it pass.
