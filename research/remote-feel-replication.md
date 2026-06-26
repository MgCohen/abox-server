# Replicating the Claude Code "Remote Control" feel

**Cold read.** Self-contained. No prior context needed.

## The setting

**A.Box** is a .NET 10 orchestrator. It drives the `claude` (and `codex`) CLI as a
child process and surfaces the running session in a separate **Box client** app.
It drives `claude` through an **interactive terminal (ConPTY/PTY)** — not headless —
because the interactive TUI is the **subscription-billing path**; the headless
`-p --output-format stream-json` mode bills differently and is off-limits
([interaction-modes.md] Q1: *"Not stream-json (forces `-p`, breaks billing)"*).

**The symptom that prompted this:** when the team mirrored "what Claude Code shows"
into the Box client, they got **garbled terminal visuals and buffer noise** instead
of a clean chat-like render.

**The goal:** make the Box client feel like Claude Code's own UI / its phone "Remote
Control" mirror — clean messages, tool cards, live streaming — *without* leaving the
billing-safe interactive path.

## What Claude Code's Remote Control actually does (verified, not inferred)

"Remote Control" mirrors a **local** `claude` session to claude.ai/web and the phone
app. Local code keeps running locally; only the conversation crosses the network.

Two layers, both confirmed by inspecting the installed binary
(`@anthropic-ai/claude-code` v2.1.179, native `claude.exe`, 225 MB):

| Layer | What it is | Evidence |
|---|---|---|
| **Transport** | **WebSocket** (`wss://`, TLS) to an Anthropic relay both ends dial *out* to (no inbound ports). | `wss://bridge.claudeusercontent.com` + full `Sec-WebSocket-*` header set in the binary. |
| **Payload** | **Anthropic Messages content blocks** — structured JSON, *not* terminal bytes. | Same model is written to the on-disk session JSONL during every run (see below). |

The `text/event-stream` / `EventSource` strings in the binary are the **separate**
model-API token-streaming path (SSE), not the remote-control bridge. The bridge is
WebSocket.

## The key insight

**The "content" is a block model you already have on disk. The terminal render is a
dead end.**

Each assistant turn is an array of typed blocks:

```
assistant.message.content = [
  { type: "text",        text },
  { type: "thinking",    thinking },
  { type: "tool_use",    id, name, input },     // input is structured JSON
  { type: "tool_result", tool_use_id, content, is_error }
]
```

This identical model is what Remote Control ships over its WebSocket **and** what
`claude` writes to `~/.claude/projects/<encoded>/<sessionId>.jsonl`, line by line,
**during the interactive billed turn**. Confirmed against a live transcript — the
block types present: `text`, `thinking`, `tool_use`, `tool_result`, wrapped in
`user`/`assistant` message lines.

**Diagnosis of the garbled visuals:** the team was rendering the **PTY buffer** — the
ANSI-stripped terminal scrape (the "de-spaced TUI buffer", oracle A7). That is the
*terminal's pixel render*, not the content. The oracle already rules: **JSONL is
authoritative; the buffer is fallback-only** (oracle A6).

## Why we don't reverse-engineer the bridge

- The bridge protocol (message framing on the WebSocket) is **private and
  undocumented**; tapping it would mean impersonating the first-party client — brittle
  and ToS-gray.
- We **can't** use `stream-json` to get the structured stream cleanly — it forces
  `-p`, which breaks subscription billing.
- We **don't need either**: the same content-block model is in the JSONL we already
  parse. The bridge is just a transport wrapper around a model we already hold.

```
Remote Control:  claude (1st-party) ──WebSocket(wss)── bridge ──── phone
                       └ taps blocks in-process        ✗ private    ✗ stream-json breaks billing

A.Box (do this): claude TUI (billed) ──writes──► session.jsonl ──tail──► blocks ──WebSocket/SignalR──► Box client
                       └ SAME content-block model, on disk
```

## Recommendations (in priority order)

### 1. Render the structured blocks, not the buffer  *(source swap — biggest win)*
The Box client should render the **block transcript**, never the PTY buffer. A.Box
already parses blocks into an `AgentTurn[]` (`{Text, Thinking, ToolUse, ToolResult}`)
in `ClaudeJsonl`. Surface *that* to the client. Keep the PTY buffer strictly internal
— it's for drive/control only (detect input-ready, dismiss startup dialogs).

### 2. Carry structured tool data, render tool cards
Today the tool_use block is flattened to a JSON string. Instead carry `name` +
typed `input` so the client renders **tool cards** like Claude Code does (Bash →
command card, FileEdit → diff, TodoWrite → checklist, etc.). The render schema is
**shipped and documented**: `sdk-tools.d.ts` in the npm package types every tool's
input/output (`BashInput`, `FileEditInput`, `FileReadOutput`, `TodoWriteInput`, …).
No reverse-engineering — it's a first-party `.d.ts`.

### 3. Tail the JSONL for live streaming
To feel live (not end-of-turn): **tail the session JSONL as it grows** and push each
new block to clients as it appears (`claude` appends lines through the turn). Replaces
"continuously scrape the buffer" with "emit structured blocks." Mind oracle A6:
Windows `FileStream` caches EOF on a growing file — **re-read, don't seek** (A.Box's
loader already does this).

### 4. Match the transport class (you already have it)
Remote Control uses **WebSocket**; A.Box already pushes events to **SignalR clients**,
and SignalR's default transport *is* WebSocket. Same class — nothing to adopt for the
LAN case.

### 5. (Only if phone-from-anywhere) copy the relay topology, not the relay
Remote Control's relay is **outbound-only from both ends**, so it crosses NAT with no
inbound ports. Plain SignalR assumes the client can reach the Host (fine on LAN,
breaks off-network). For phone reach: put a relay in the middle or tunnel — the
`cloudflared`/mesh path already sketched in `design/remote-access.md`. Not needed for
the in-house client on the same network.

## What is NOT replicable (set expectations)

| Thing | Why not |
|---|---|
| Connecting to `bridge.claudeusercontent.com` | Anthropic-private relay, claude.ai-OAuth-authed, undocumented framing. |
| A clean `stream-json` feed from the billed turn | `stream-json` forces `-p`, which breaks subscription billing. |
| Remote Control's credential model | Requires full-scope `/login` OAuth; explicitly rejects `claude setup-token`, which A.Box's sandbox/billing is built on. |

## The short version

You're ~3 small steps from the Claude Code feel, and none of them need the bridge:
1. **Render `AgentTurn[]`, not the PTY buffer.**
2. **Enrich tool blocks** (`name` + typed `input`) → tool cards, schema'd by
   `sdk-tools.d.ts`.
3. **Live-tail the JSONL** → push blocks over your existing SignalR/WebSocket.

The transport you already have (WebSocket via SignalR). The content model you already
parse (JSONL blocks). The only real bug was rendering the terminal instead of the
data.

---

### Evidence trail (for the next reader to re-verify)

- Binary: `…/@anthropic-ai/claude-code/node_modules/@anthropic-ai/claude-code-win32-x64/claude.exe` (v2.1.179).
- Transport string: `wss://bridge.claudeusercontent.com` (+ staging) in the binary.
- Render schema: `…/@anthropic-ai/claude-code/sdk-tools.d.ts`.
- Block model on disk: `~/.claude/projects/<encoded-cwd>/<sessionId>.jsonl`.
- A.Box parser: `src/Domain/Agents/Claude/ClaudeJsonl.cs` (`AgentTurn[]`).
- Billing constraint: `PLANS/interaction-modes.md` Q1.
- Buffer-is-fallback rule: `design/behavioral-oracle.md` A6/A7.
