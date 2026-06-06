# Structured Agent Questions — Spike

Throwaway probe for [`PLANS/structured-questions-spike.md`](../../PLANS/structured-questions-spike.md).
Validates that `claude` and `codex` can be steered to emit a structured
`<<NEEDS_INPUT>>` + JSON envelope when blocked, that we can parse it into a typed
`AgentQuestion`, and that resuming the session keeps context — all at the output
level, no hooks, no live terminal loop.

**This is not engine code.** `AgentQuestion.cs` + `QuestionParser.cs` are written
dependency-free so they can be lifted into `src/` verbatim when hardening.

## Layout

```
AgentQuestion.cs      §3 types (liftable)
QuestionParser.cs     §5 parser + Diagnose() for metrics (liftable)
Directive.txt         §6 unattended directive appended to both providers
prompts.json          §7 corpus (id, prompt, expected status/kind)
RunClaude.ps1         §6 Claude driver (Windows-native; replaces run-claude.sh)
RunCodex.ps1          §6 Codex driver (Windows-native; replaces run-codex.sh)
sandbox-template/     throwaway repo context; agents run HERE, never the real repo
Harness/              C# console: loops matrix, runs the real parser, scores, reports
out/<tag>/            captures + results.json + summary.md (git-ignored)
```

## Why PowerShell drivers (not the plan's `.sh`)

This box is Windows with no WSL, and `uuidgen` + `jq` are absent. The C# harness
generates session ids via `Guid.NewGuid()` and parses JSON natively; the drivers
are thin PowerShell because `.NET`'s `Process` can't launch the `claude.cmd` /
`codex.cmd` shims directly and the directive is multiline. Same commands as §6,
Windows-native.

## Safety

Every agent run executes in a **fresh copy of `sandbox-template/`** under
`out/<tag>/sandbox/`, reset before each run. The prompts ask the agent to *do
work* (create csprojs, edit files, "deploy") under `--permission-mode acceptEdits`
/ Codex autonomous mode — so they must never point at the real repo.

## Run

```powershell
# 1. Parser fixtures — no agents, no tokens. Do this first.
dotnet run --project Harness -- selftest

# 2. Cheap live smoke (one positive + one negative, claude only).
dotnet run --project Harness -- partA --providers claude --prompts open-1,none-1 --n 1 --tag smoke

# 3. Full Part A matrix (emission reliability). Scale N as token budget allows.
dotnet run --project Harness -- partA --n 5 --tag partA

# 4. Part B continuity (resume keeps context).
dotnet run --project Harness -- partB --prompts choice-1,open-1 --tag continuity
```

Results: `out/<tag>/summary.md` (§10 tables) + `results.json` (raw, scored) +
per-run `*.final.txt` / `*.json` / `*.events.jsonl` captures.

Thresholds (from §7): parse rate ≥95%, false-positive ≈0%.
