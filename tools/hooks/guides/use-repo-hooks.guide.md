---
docType: guide
---

## Summary
How to react to repository and agent lifecycle events — a commit landing, a Claude turn ending — by
dropping a declarative `.hook` file in your feature's own folder. A `.hook` needs no build, no
registration, and no code change: the `abox-hooks` controller (`tools/hooks`) discovers it on disk and
runs its action — a shell `run:` command handed the event as JSON on stdin, or a fresh `agent:` — so the
reaction can be in any language.

## Procedures
### Adding a hook
**Context:** a `.hook` is a declarative text file (`<name>.hook`) the `abox-hooks` controller discovers
by globbing its scan roots. It names the event kinds that fire it and an action to run; the event
arrives as JSON on a `run:` command's stdin, so the reaction can be in any language.
##### 1. Choose the event kind to react to
Pick a wired kind for the `on:` field: `CommitLanded` (a git commit landed) or `TurnEnded` (a Claude
agent turn ended).
##### 2. Write the .hook file in your feature's folder
Create `<feature>/<name>.hook` with `on:` (required — the event kinds) and exactly one action: `run:` (a
shell command) or `agent:` (a prompt for a fresh reviewer). Optionally add `when:` (a `source` / `cwd
glob` / `tool` filter) and `mode:` — `notify` (default; async, result ignored), `gate`, or `check`
(synchronous; the action's output is fed back to the running agent, and a non-zero exit blocks the turn).
##### 3. Opt the repo in
Create an `.abox/` directory at the repo root. Producers emit events only when `.abox/` exists, so a
repo without it stays silent.
##### 4. Dispatch the pending events
Run `abox-hooks run --root <feature>` to read `.abox/hooks.jsonl`, match your `.hook`, and run its
`run:` command with the event on stdin.

**Outcome:** `abox-hooks run` prints `abox-hooks: dispatched N event(s)` and your `run:` command's side
effect — its output, or the file it wrote — is present, so your command now runs whenever a matching
event is on the stream, with the event JSON on its stdin.

---

### Reacting when a commit lands
**Context:** the `CommitLanded` event is produced by a git `post-commit` hook that calls
`abox-hooks commit`, which reads `HEAD` and dispatches in one step.
##### 1. Install the git post-commit hook
Run `abox-hooks install-git` in the repo. It writes `.git/hooks/post-commit`; if the repo uses a
custom `core.hooksPath` it refuses and tells you to wire the hook by hand.
##### 2. Add a hook on CommitLanded
Following "Adding a hook", create a `.hook` with `on: [CommitLanded]` and your `run:` command, and make
sure the repo has opted in with an `.abox/` directory.
##### 3. Make a commit
Run `git commit`; the post-commit hook fires `abox-hooks commit` automatically.

**Outcome:** the commit prints `abox-hooks: CommitLanded <sha> → dispatched N event(s)` and a
`CommitLanded` line appears in `.abox/hooks.jsonl`, so every commit now runs your hook with the commit's
sha, branch, and subject on stdin.

---

### Reacting when a Claude turn ends
**Context:** the Claude provider maps each turn's raw Stop signal onto the hooks stream as a
`TurnEnded` event — but only when the project has opted in.
##### 1. Opt the project in
Ensure an `.abox/` directory exists in the project the agent runs against; without it the provider
emits nothing.
##### 2. Add a hook on TurnEnded
Following "Adding a hook", create a `.hook` with `on: [TurnEnded]` and your `run:` command.
##### 3. Run an agent turn
Drive a Claude agent turn against that project as you normally would.

**Outcome:** a `TurnEnded` line appears in `.abox/hooks.jsonl` and your hook's side effect is present, so
your command now runs at the end of each Claude turn with the raw turn payload on stdin.

---

### Feeding the agent back with a check hook
**Context:** a `mode: check` hook runs synchronously and its output is relayed to the running agent; a
non-zero exit blocks the turn-end so the agent must address it before stopping, while a passing check
surfaces its output as advisory context.
##### 1. Write a check hook
Following "Adding a hook", create a `.hook` with `mode: check` and a `run:` command that prints a message
and exits non-zero — for example `run: echo "doc invalid" && exit 2`.
##### 2. Trigger a turn end against the opted-in repo
With the repo opted in (an `.abox/` directory), pipe a Claude Code Stop payload into `abox-hooks
turn-ended --repo .` to emit a `TurnEnded` event and dispatch your check.

**Outcome:** `abox-hooks turn-ended` exits non-zero (2) and prints the check's message on stderr; flip
the command to exit 0 and it instead prints a JSON `additionalContext` carrying the message — so a
failing check blocks the turn and feeds its message back, while a passing check advises.

---

### Narrowing a hook with a when: filter
**Context:** `on:` selects the event kind; `when:` adds a closed-vocabulary filter so the hook fires
only on a subset. Every present `when:` clause must hold for the hook to run.
##### 1. Add a when: clause
Add a `when:` line to your `.hook` with exactly one of `source <claude|codex|git>`, `cwd glob
"<pattern>"`, or `tool <name>`.
##### 2. Re-dispatch against matching and non-matching events
Run `abox-hooks run` over an event that should match and one that should not.

**Outcome:** the hook runs for the matching event and is skipped for the non-matching one (no side
effect), so it now reacts only to events that satisfy its `when:` filter.
