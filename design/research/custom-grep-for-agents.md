# Research: Replacing the agents' grep with a custom search

**Question.** Can we replace the grep our agents (`claude`/`codex`) use, so we can
run a custom grep with custom behavior on searches — e.g. blocking certain
searches, scoping/redacting results, or swapping in different search semantics?

**Status.** Research only. No code changed. Captures the design space and a
recommendation so we can decide before building.

---

## The asymmetry that drives everything

"grep" is not the same kind of thing in the two agents:

- **Claude** has a discrete, named `Grep` tool. It is gateable and replaceable.
- **Codex** has *no* `Grep` tool at all. Its search *is* the `shell` tool running
  `rg`/`grep`. See `src/RemoteAgents/Actors/Agents/Codex/CodexProtocol.cs:94`
  (`command_execution` → `{"name":"shell", ...}`). There is nothing named "Grep"
  to disable — search is just another shell command.

This single fact splits the work into two very different cost classes: policy on
Claude is cheap (typed tool), policy on Codex is brittle (parsing command
strings), and *new semantics* on either is only reachable one way (MCP).

---

## How tooling reaches the agents today

We don't reimplement the CLIs' tools — we drive the real `claude`/`codex` CLIs
over ConPTY and only *influence* their tools.

- **Claude.** `ClaudeHooks.Create` generates a `settings.json` with hooks and
  passes it via `--settings`
  (`src/RemoteAgents/Actors/Agents/Claude/ClaudeProtocol.cs:48`). A `PreToolUse`
  matcher gates a hardcoded set of tools
  (`src/RemoteAgents/Actors/Agents/Claude/ClaudeHooks.cs:17`,
  `GatedTools = ["Bash", "Write", "Edit", "MultiEdit"]`). The matcher string is
  `string.Join('|', GatedTools)` (`ClaudeHooks.cs:132`). A gated call is piped to
  a PowerShell shim → dropped as a `req-*.json` → drained by the provider
  (`ClaudeHooks.DrainRequests`) → routed to a resolver / `AutoPolicy` →
  `resp-*.json` returns the allow/deny decision.
- **Codex.** `CodexProtocol.BuildArgs` runs `codex exec` with a baked-in sandbox
  (`workspace-write` on Linux, `danger-full-access` on Windows). No per-tool
  gating exists in this codebase today.

So today we **allow/deny** tools; we don't **provide** them.

---

## The three mechanisms available

### 1. Hook interception — extends what we already have
Add `"Grep"` to `GatedTools` (`ClaudeHooks.cs:17`). Every search then flows
through the existing shim → `DrainRequests` → resolver → `AutoPolicy` path that
Bash/Write already use. The hook can **allow/deny** (and, on current Claude Code,
potentially rewrite the tool input — verify against the installed CLI version;
today the code only ever allows/denies).

- ✅ Great for **blocking** searches and **scoping** them (deny or narrow
  `path`/`glob`/`pattern`).
- ⚠️ Cannot cleanly **redact results** — `PreToolUse` decides *whether* the real
  Grep runs; it does not rewrite the output the real Grep produces.
- Claude-only. Would extend `ClaudePermission.Detail`
  (`src/RemoteAgents/Actors/Agents/Claude/ClaudePermission.cs`) to pull
  `pattern`/`path`/`glob`, and add Grep rules to `AutoPolicy`'s denylist.

### 2. Custom MCP search tool + disable native `Grep` — the real "replacement"
Stand up an MCP server exposing e.g. `search`, register it (settings.json
`mcpServers` / `--mcp-config`), and deny the built-in (`--disallowedTools Grep`
or `permissions.deny`). Now *our code* runs the search and returns the bytes — we
own ranking, indexing, Unity-aware filtering, **and** result redaction.

- ✅ The **only** path that delivers new semantics *and* output redaction.
- ✅ The **only** path uniform across Claude **and** Codex (both speak MCP), and it
  sidesteps the "Codex has no Grep" problem by *giving* it a tool it didn't have.
- 💰 Cost: a new shippable MCP process + wiring `--mcp-config` into both
  `BuildArgs`. A genuine new mechanism — against the repo's YAGNI rule unless the
  semantics actually need it.

### 3. Shell-command gating — Codex's only lever for policy
To block/scope search on Codex without MCP, we'd inspect `shell` command strings
for `rg`/`grep` and gate those — string-parsing commands, exactly the brittle
thing the Claude hook avoids by having a typed tool. Not recommended; reach for
#2 instead.

---

## How the desired behaviors map

| Behavior | Cleanest mechanism | Claude | Codex |
|---|---|---|---|
| **Block certain searches** | Hook gate (#1) | reuse `GatedTools` + `AutoPolicy` | shell-gating (#3) or MCP (#2) |
| **Scope / redact** | scope → hook; **redact → MCP** | scope via hook, redact via MCP tool | MCP only |
| **New engine / semantics** | MCP tool (#2) | disable `Grep`, add MCP server | add MCP server |

---

## Recommendation

The dividing line is **redaction + new semantics vs. pure policy.**

- If the goal is *policy* (block/scope): do **#1**. A few lines extending
  machinery that already exists (`AutoPolicy`, the resolver, `ClaudePermission`),
  Claude-only, and the codebase is already shaped for it.
- If the goal is *a different search* (custom ranking, Unity-aware, scrubbed
  output): there is no shortcut — **#2**, an MCP search tool, is the answer, and
  it has the bonus of working identically for Claude and Codex because it stops
  treating "grep" as a CLI built-in and starts treating search as *our*
  capability.

Per the repo's YAGNI / least-mechanism rule: don't build the MCP server for pure
blocking. Match the requirement in front of us.

---

## Possible next steps (not yet decided)

- Sketch the concrete diff for #1 (small, low-risk): `Grep` → `GatedTools`,
  `pattern`/`path`/`glob` extraction in `ClaudePermission.Detail`, Grep rules in
  `AutoPolicy`.
- Prototype a minimal MCP search server for #2 to see what the "custom behavior"
  surface looks like before committing to it.

## Key references

- `src/RemoteAgents/Actors/Agents/Claude/ClaudeHooks.cs:17,132` — gated-tools set
  + matcher.
- `src/RemoteAgents/Actors/Agents/Claude/ClaudeProtocol.cs:40-50` — Claude
  `BuildArgs` (`--settings`, `--permission-mode`, `--model`).
- `src/RemoteAgents/Actors/Agents/Claude/ClaudePermission.cs` — tool-payload
  field extraction for display/guardrails.
- `src/RemoteAgents/Actors/Agents/Claude/AutoPolicy.cs` — denylist guardrail.
- `src/RemoteAgents/Actors/Agents/Codex/CodexProtocol.cs:8-41,94` — Codex
  `BuildArgs` + the `shell`-only tool model.
- `design/adr/0007-permission-policy-pretooluse.md` — permission-gating design.
