---
docType: guide
---

## Summary
How to use repo-hooks: react to repository and agent lifecycle events — a commit landing, a Claude
turn ending — by dropping a declarative `.hook` file in your feature's own folder. Each action below
is independent; start from the one matching what you want to do. Every action ends in a command you
can run to confirm it worked. The controller is the built `abox-hooks` CLI (`tools/hooks`); a `.hook`
needs no build, no registration, and no code change — it is discovered on disk and its `run:` command
receives the event as JSON on stdin.

## Actions
### Add a hook
- **Context:** a `.hook` is a declarative text file (`<name>.hook`) the `abox-hooks` controller
  discovers by globbing its scan roots. It names the event kinds that fire it and a shell command to
  run; the event arrives as JSON on that command's stdin, so the reaction can be in any language.
#### Choose the event kind to react to
<!-- id: 1 -->
Pick a wired kind for the `on:` field: `CommitLanded` (a git commit landed) or `TurnEnded` (a Claude
agent turn ended).
#### Write the .hook file in your feature's folder
<!-- id: 2 -->
Create `<feature>/<name>.hook` with `on:` (required — the event kinds) and exactly one action: `run:`
(a shell command) or `agent:` (a prompt for a fresh, minimal-context reviewer). Optionally add `when:`
(a `source` / `cwd glob` / `tool` filter) and `mode:` — `notify` (default; async, result ignored),
`gate` (the synchronous perm-shim), or `check` (synchronous; the action's output is fed back to the
running agent, and a non-zero exit blocks the turn).
#### Opt the repo in
<!-- id: 3 -->
Create an `.abox/` directory at the repo root. Producers emit events only when `.abox/` exists, so a
repo without it stays silent.
#### Dispatch the pending events
<!-- id: 4 -->
Run `abox-hooks run --root <feature>` to read `.abox/hooks.jsonl`, match your `.hook`, and run its
`run:` command with the event on stdin.
- **Validation:** `abox-hooks run` prints `abox-hooks: dispatched N event(s)` and your `run:`
  command's side effect — its output, or the file it wrote — is present.
- **Outcome:** your command runs whenever a matching event is on the stream, with the event JSON on its stdin.

### React when a commit lands
- **Context:** the `CommitLanded` event is produced by a git `post-commit` hook that calls
  `abox-hooks commit`, which reads `HEAD` and dispatches in one step.
#### Install the git post-commit hook
<!-- id: 1 -->
Run `abox-hooks install-git` in the repo. It writes `.git/hooks/post-commit`; if the repo uses a
custom `core.hooksPath` it refuses and tells you to wire the hook by hand.
#### Add a hook on CommitLanded
<!-- id: 2 -->
Following "Add a hook", create a `.hook` with `on: [CommitLanded]` and your `run:` command, and make
sure the repo has opted in with an `.abox/` directory.
#### Make a commit
<!-- id: 3 -->
Run `git commit`; the post-commit hook fires `abox-hooks commit` automatically.
- **Validation:** the commit prints `abox-hooks: CommitLanded <sha> → dispatched N event(s)` and a
  `CommitLanded` line appears in `.abox/hooks.jsonl`.
- **Outcome:** every commit runs your hook with the commit's sha, branch, and subject on stdin.

### React when a Claude turn ends
- **Context:** the Claude provider maps each turn's raw Stop signal onto the hooks stream as a
  `TurnEnded` event — but only when the project has opted in.
#### Opt the project in
<!-- id: 1 -->
Ensure an `.abox/` directory exists in the project the agent runs against; without it the provider
emits nothing.
#### Add a hook on TurnEnded
<!-- id: 2 -->
Following "Add a hook", create a `.hook` with `on: [TurnEnded]` and your `run:` command.
#### Run an agent turn
<!-- id: 3 -->
Drive a Claude agent turn against that project as you normally would.
- **Validation:** a `TurnEnded` line appears in `.abox/hooks.jsonl` and your hook's side effect is present.
- **Outcome:** your command runs at the end of each Claude turn, with the raw turn payload on stdin.

### Feed the agent back with a check hook
- **Context:** a `mode: check` hook runs synchronously and its output is relayed to the running agent;
  a non-zero exit blocks the turn-end so the agent must address it before stopping, while a passing
  check surfaces its output as advisory context.
#### Write a check hook
<!-- id: 1 -->
Following "Add a hook", create a `.hook` with `mode: check` and a `run:` command that prints a message
and exits non-zero — for example `run: echo "doc invalid" && exit 2`.
#### Trigger a turn end against the opted-in repo
<!-- id: 2 -->
With the repo opted in (an `.abox/` directory), pipe a Claude Code Stop payload into `abox-hooks
turn-ended --repo .` to emit a `TurnEnded` event and dispatch your check.
- **Validation:** `abox-hooks turn-ended` exits non-zero (2) and prints the check's message on stderr;
  flip the command to exit 0 and it instead prints a JSON `additionalContext` carrying the message.
- **Outcome:** a failing check blocks the turn and feeds its message back to the agent; a passing check
  surfaces its output as advisory context the agent sees but is not blocked by.

### Narrow a hook with a when: filter
- **Context:** `on:` selects the event kind; `when:` adds a closed-vocabulary filter so the hook fires
  only on a subset. Every present `when:` clause must hold for the hook to run.
#### Add a when: clause
<!-- id: 1 -->
Add a `when:` line to your `.hook` with exactly one of `source <claude|codex|git>`, `cwd glob
"<pattern>"`, or `tool <name>`.
#### Re-dispatch against matching and non-matching events
<!-- id: 2 -->
Run `abox-hooks run` over an event that should match and one that should not.
- **Validation:** the hook runs for the matching event and is skipped for the non-matching one — no side effect.
- **Outcome:** the hook reacts only to events that satisfy its `when:` filter.
