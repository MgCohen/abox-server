---
docType: rulebook
testType: unit
rubric: ../../../../tests/Rubrics/Unit.md
harness: ../../../../tests/Harness/README.md
---

## Rules

### Glob matches ** across directories and * within a single segment
- **Why:** the `when: cwd glob` filter decides which sessions a hook reacts to, so `**` must span nested
  directories while `*` stays inside one segment — collapse the distinction and a hook fires on the wrong tree.

### HookManifestParser parses a well-formed .hook into its kinds, when-filter, mode, and run command
- **Why:** the `.hook` file is the whole feature-facing seam; if the parser drops the `on:` kinds, the `when:`
  filter, the mode, or the run command, a feature's declared intent is silently not what the controller runs.

### HookManifestParser rejects a .hook missing on: or run: with an actionable error naming the file
- **Why:** `on:` and `run:` are the two load-bearing fields — a hook with neither an event to fire on nor a
  command to run is inert, so it must fail loudly, naming the file, not be discovered as a silent no-op.

### HookEvent round-trips through its jsonl line, preserving kind, source, ids, and raw payload
- **Why:** the jsonl line is the transport between the dumb shim and the controller; a consumer reads the kind,
  source, and ids and may reach into the raw payload, so the line must reconstruct the event without loss.

### HookEvent.TryParse rejects a malformed or kindless line
- **Why:** the log is appended to by external shims and can carry a torn or junk line; parsing must reject it
  by returning false rather than throwing, so one bad line never aborts the whole dispatch pass.

### HookManifest.Matches gates on event kind and the when-filter
- **Why:** matching is the controller's core decision — a hook must run only when the event kind is in its
  `on:` set and every present `when:` clause (source, cwd glob, tool) holds, never on a near-miss.

### HookCatalog discovers .hook files under its scan roots and reports a malformed one instead of throwing
- **Why:** discovery is convention-over-registration across many feature folders; one malformed `.hook` must be
  reported and skipped, leaving the rest discoverable, so a single typo can't blind the controller to every hook.

### HookDispatcher runs only the matching react hooks, feeding the event on stdin
- **Why:** `react` hooks fan out off the event with the payload on stdin; a non-matching hook or a `gate` hook
  (which rides the synchronous perm-shim, not this stream) must not be run here, or the wrong code fires.

### HookController dispatches the pending log slice once and advances the cursor past completed lines
- **Why:** the durable cursor is what makes delivery deferred-not-dropped — it must advance past completed lines
  after dispatch (so a line isn't re-run forever) yet leave a torn trailing line for the next pass.

### GitInstaller.InstallPostCommit on a default repo → writes an executable post-commit that calls abox-hooks commit
- **Why:** the git source is wired by a post-commit hook; the installer must drop an executable hook that calls
  back into the CLI, or commits never produce a CommitLanded event and the whole git path is inert.

### GitInstaller.InstallPostCommit on a repo with a custom core.hooksPath → refuses without writing a hook
- **Why:** a repo with a managed hooks dir (this one uses governance `.githooks`) must not be silently hijacked —
  the installer refuses and tells the user to wire it by hand, rather than clobbering or being silently ignored.

### abox-hooks commit in an opted-in repo → appends a CommitLanded line and dispatches matching hooks
- **Why:** this is the post-commit entry point and the git half of the transport — it must read HEAD, append a
  well-formed CommitLanded line carrying the sha, and dispatch so a commit triggers its reactions immediately.

### abox-hooks commit with no .abox opt-in → emits nothing
- **Why:** emission is opt-in per repo (an `.abox/` dir), so a commit in a repo that wants no hooks must leave no
  hooks.jsonl behind — the same opt-in contract the Claude turn-end emit honors.

### ClaudeCodeInstaller.InstallStopHook → wires a turn-ended Stop hook into settings, preserving existing keys
- **Why:** this is how repo-hooks fires in a normal Claude Code session (not just orchestration) — it must add a
  `Stop` hook that calls `turn-ended` while merging into existing settings, never clobbering other keys or hooks.

### ClaudeCodeInstaller.InstallStopHook on a settings already wired → no duplicate Stop hook
- **Why:** install must be idempotent — re-running it (or running it in an already-set-up repo) must not stack
  duplicate Stop hooks that would emit the same event many times per turn.

### abox-hooks turn-ended in an opted-in repo → appends a TurnEnded line from the Stop payload and dispatches
- **Why:** this is the dev-loop producer — given the Claude Code Stop payload on stdin it must emit a well-formed
  TurnEnded line (carrying the session id and raw payload) and dispatch, so a normal session fires hooks too.

### abox-hooks turn-ended with no .abox opt-in → emits nothing
- **Why:** a Stop hook fires every turn, so without the `.abox/` opt-in it must be a silent no-op — never writing
  a stream into a repo that did not ask for one.
