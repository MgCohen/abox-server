# Active Context Management for AI Agents — A 2026 Research Report

> Deep-research synthesis (fan-out web search → adversarial verification → cited
> synthesis). Produced 2026-06-09. Five parallel research angles: Claude API
> internals, Claude Code CLI + Agent SDK, OpenAI Codex CLI + Responses API,
> context-engineering patterns, OSS save/restore/fork tooling. Confidence tags:
> **[H]** = official vendor docs or 2+ independent sources, **[M]** = single decent
> source or comparative judgment, **[L]** = inferred / negative finding / single
> weak source.
>
> **Motivating question:** for an orchestrator that drives the `claude`/`codex`
> CLIs over ConPTY on a subscription (not raw API), what is the validated toolset
> to (1) deterministically inspect a context window, (2) copy/fork a built-up
> context to a sibling agent, and (3) inject a primed starting context into a
> fresh agent — beyond "agent, read this file"?

---

## TL;DR

- **There is no mechanism — in any vendor's stack — to snapshot, copy, or inject a
  model's *internal* context-window state** (activations / KV-cache). The "clone
  the live window like a VM" idea does not exist as a primitive and won't, for
  architectural reasons. **[H]**
- **"Context" universally means the ordered message/token log.** Therefore:
  inspect = read the log; fork = copy the log and replay it; inject = construct a
  log and feed it; isolate = give a child its own log. Every "fork" feature in the
  wild (`--fork-session`, `previous_response_id` branching, LangGraph `update_state`,
  Codex rollout replay) is *serialize-the-log + replay/branch-it* underneath. **[H]**
- **Two opposed API state models.** Anthropic Messages API is **stateless** (client
  resends the full array; byte-level ownership, no TTL). OpenAI Responses API is
  **server-side** (`previous_response_id` chains + `store:true` + Conversations
  objects). OpenAI has a *documented* fork affordance; Anthropic gives finer
  ownership. Roughly equal capability, different ergonomics. **[H]**
- **None of the API-layer state helps a ConPTY/subscription setup directly** — your
  real levers are the CLI commands and the on-disk session JSONL transcripts. **[H]**
- **Claude Code has the strong CLI story:** a complete JSONL transcript per session,
  a shipped native fork (`--fork-session` / `/branch`), subagents for isolation,
  and `/context` for inspection. **Codex's CLI is weaker** — sequential `resume`
  + experimental rollout replay, no fork command, auto-generated session ids. **[H]**
- **Mutating early context is actively harmful** (Manus): one-token prefix changes
  invalidate KV-cache (~10× cost swing) and dangling tool references desync the
  model. Durable discipline = **append-only logs + restorable external memory**,
  never in-place context surgery. **[H]**
- **The multi-agent trade-off is contested.** Anthropic favors isolated sub-agents
  (clean windows, parallelism, ~15× tokens); Cognition warns sub-agents act on
  conflicting implicit assumptions and recommends a single-threaded agent +
  compression. A true *full-transcript fork* sidesteps Cognition's critique; a
  *fresh subagent with a summary* is exactly what it warns about. **[H]**
- **LangGraph is the OSS gold standard** for deterministic save/restore/**fork**
  (checkpointers + `update_state` branching) — worth modeling even from .NET. **[H]**

---

## 1. The one load-bearing finding

No public LLM stack exposes the model's *internal* state (activations / KV-cache)
for copying. What every system calls "context" is **the message log** — the array
of `{role, content-blocks}` re-fed to the model each turn. So the entire design
space collapses to log operations:

| Operation you want | What it actually is |
|---|---|
| Inspect a context window | Read the message log |
| Fork / duplicate a context | Copy the message log and replay it |
| Inject a starting context | Construct a message log and feed it in |
| Isolate context | Give a child agent its own log/window |

The KV-cache is a *transparent performance shadow* of the token prefix: two
requests sharing a prefix automatically share cache, but you cannot grab that
cache and graft it onto a divergent branch ([Manus][manus]). Independent
implementations confirm the same mechanism — reconstruct from the message log,
never copy model internals ([Codex #18023][codex-pr], [Zed #27967][zed-pr],
[agentsview #107][agentsview]). **[H]**

---

## 2. Two state models you build across

| | **Anthropic / Claude** | **OpenAI / Codex** |
|---|---|---|
| API state | **Stateless** — resend full `messages[]` each call ([docs][cl-msg]) | **Server-side** — `previous_response_id` + `store:true` + Conversations ([docs][oai-state]) |
| Where context lives | The array you hold (byte ownership, no TTL) | OpenAI servers (30-day TTL bare responses; **unlimited** for conversation objects) |
| Fork primitive | Copy the array and diverge (no named API primitive) | **Documented**: point new responses at the same `previous_response_id` — cookbook says *"fork the response at any point"* ([cookbook][oai-cookbook]) |
| Token cost of fork | You manage the array/budget | Still **re-bills all parent tokens** each turn ([docs][oai-state]) |
| Instructions across link | n/a (you own the array) | `previous_response_id` does **not** carry top-level `instructions` — resend them ([docs][oai-state]) **[M]** |

**Verdict:** OpenAI offers a cleaner first-class fork *affordance*; Anthropic offers
finer *ownership* (exact bytes, no backend dependency, no TTL). Capability is
roughly equal — both are "the log is forkable." **Neither API column is reachable
from a ConPTY/subscription setup** — it's context for what's theoretically possible.

---

## 3. Claude API internals (raw API — reference only for subscription users)

- **Stateless conversation model.** Send full `messages[]` every request; no
  server-side session. History = array of `{role, content}` where content is a
  string or typed blocks (`text`, `image`, `tool_use`, `tool_result`, `document`,
  `thinking`). Round-trippable: serialize the array, replay it, reconstruct an
  identical context. ([docs][cl-msg], [messages API][cl-api]) **[H]**
- **Load-bearing replay caveat:** `thinking` blocks carry an opaque `signature`
  that must be preserved verbatim in multi-turn tool flows or the API rejects them.
  "Identical replay" means keeping full block objects, not flattened text. **[H]**
- **Prompt caching is NOT a snapshot.** `cache_control:{type:"ephemeral"}` marks a
  prefix breakpoint (max 4); TTL 5 min default / 1 h optional; min cacheable prefix
  ~1024–4096 tokens. It caches the computed prefill of a prefix — a cost/latency
  optimization, **not** a forkable checkpoint. There is no "cache id" to resume
  from. ([prompt caching][cl-cache]) **[H]**
- **Context editing (beta `context-management-2025-06-27`)** auto-*prunes* stale
  content (`clear_tool_uses_20250919`, `clear_tool_inputs`, `clear_thinking_...`) —
  the opposite of injection; reported 84% token reduction on a 100-turn eval.
  ([context editing][cl-ctxedit], [news][cl-ctxnews]) **[H]**
- **Memory tool (`memory_20250818`)** = client-side file directory (`/memories`)
  the model reads/writes across sessions; *you* implement storage. Genuine
  cross-session persistence, but file-based app state read via tool calls — not a
  model-state snapshot. ([memory tool][cl-memory]) **[H]**
- **Files API** (`files-api-2025-04-14`): upload once, reference `file_id` in many
  requests. A content-delivery convenience (still billed as input tokens when
  referenced), not a conversation-state store. ([files][cl-files]) **[H]**
- **Token counting** (`POST /v1/messages/count_tokens`): measure context size
  before sending; free, rate-limited. It's an **estimate** — billed tokens may
  differ and un-billed system-optimization tokens may be added. Don't use
  `tiktoken` (OpenAI tokenizer, undercounts ~15–20%). ([token counting][cl-tokens]) **[H]**

**Net:** the only true inject/fork primitive is **resending the serialized
`messages[]`**. Everything else is adjacent. No API surface forks model internals.

---

## 4. What you actually have — Claude Code CLI + Agent SDK

The strong story, and where your orchestrator lives.

- **The transcript IS the deterministic context record.** Append-only JSONL at
  `~/.claude/projects/<encoded-cwd>/<session-id>.jsonl` (cwd path, every
  non-alphanumeric char → `-`; override root with `CLAUDE_CONFIG_DIR`; auto-removed
  after 30 days via `cleanupPeriodDays`). Each line carries `type`, `uuid`,
  **`parentUuid`** (the DAG/tree links that enable branching), `timestamp`,
  `sessionId`, `cwd`, and a `message` with role + content blocks (`text`,
  `tool_use` with exact inputs, `thinking`); tool results are separate records with
  `toolUseResult`. ([sessions][cc-sessions], [format teardown][cc-format] **[M]**) **[H]**
- **Native fork — the shipped sibling-agent primitive.** `claude --resume <id>
  --fork-session` / `claude --continue --fork-session`; in-session `/branch [name]`
  (prints new + original session ids). Copies the conversation-so-far under a **new
  session id, original untouched** → two independent agents sharing built-up
  context. Caveats: per-session "allow" permissions do *not* carry to the fork;
  resuming the same id in two terminals *without* forking interleaves both into one
  transcript — to fan out you **must** fork. ([sessions][cc-sessions]) **[H]**
- **Subagents = isolation primitive.** The `Task` tool spawns a child with its own
  fresh window, own CLAUDE.md/tools copy, own permission mode; **only its final
  text response returns to the parent** (docs example: child read 6,100 tokens,
  parent got 420). The child does **not** inherit parent history — isolation, not
  cloning. ([context window][cc-ctx], [sub-agents][cc-sub]) **[H]**
- **In-session inspection & compaction.** `/context` (live token breakdown by
  category + tips), `/memory`, `/compact [instructions]`, `/clear`. Auto-compaction
  fires near the limit (~95%); preserves intent/decisions/files-modified/errors,
  drops verbatim tool output. What survives: system prompt + project-root CLAUDE.md
  + auto-memory are **re-injected from disk**; `paths:`-scoped/nested CLAUDE.md is
  **lost until a matching file is re-read**; skill bodies re-injected but capped
  (5k/skill, 25k total). Default window 200K; Opus/Sonnet 4.6+ support a 1M variant.
  ([context window][cc-ctx]) **[H]**
- **Agent SDK (programmatic, if you leave ConPTY):** `query()` options `resume` /
  `continue_conversation` / **`fork_session=True`**; `list_sessions()` /
  `get_session_messages()` to read transcripts; `session_id` from the result
  message. Sessions persist to the same `~/.claude/projects/...` path (cwd must
  match to resume). **Removed in TS SDK 0.3.142:** experimental `createSession()`
  V2 and `resumeSessionAt` — don't design around them. Cross-host resume = move the
  JSONL to the same path, **or** (docs' "often more robust") distill state and
  re-prompt. ([SDK sessions][cc-sdk], [session storage][cc-store]) **[H]**
- **Headless I/O:** `claude -p --output-format json|stream-json` emits NDJSON of
  every event; `--input-format stream-json` accepts a message stream — but whether
  you can inject an arbitrary *pre-built assistant/tool history* (vs only user
  turns) is **undocumented** (open issue [#24594][cc-issue]). **[M]**

---

## 5. Codex CLI + Responses API — the weaker CLI story

- **Server-side state (API).** `previous_response_id` chains; `store:true` default
  (30-day retention); newer **Conversations API** (`conv_…` objects) persists items
  **indefinitely** (no 30-day TTL), reusable across sessions/devices/jobs.
  ([conversation state][oai-state], [conversations][oai-conv]) **[H]**
- **Forking (API) is documented & demonstrated** — point multiple new responses at
  one `previous_response_id`; divergent children form a branch tree; fork point
  persists server-side. The Conversations *object* is a single linear thread, so the
  clean fork lives in the **response-id graph**, not the conversation object.
  ([cookbook][oai-cookbook]) **[H / M]**
- **Codex CLI rollout files.** JSONL at `~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl`
  capturing messages, tool calls/outputs, command execs, file changes, approvals,
  plan history, token usage; on resume the ContextManager replays them. Commands:
  `codex resume [--last|<id>|--all]`, `codex exec resume --last "<prompt>"`.
  ([codex features][codex-cli], [discussion][codex-disc] **[M]**) **[H]**
- **No first-class fork at the CLI; session ids are backend auto-generated** (can't
  name them). Closest to "load an arbitrary saved context" is experimental
  `-c experimental_resume="<rollout.jsonl>"`. So **the clean fork lives in Codex's
  *API*, not its CLI.** ([resume guide][codex-resume] **[M/L]**) **[L]**
- **AGENTS.md = Codex analog of CLAUDE.md** — always-on instructions loaded before
  the prompt; global `~/.codex/AGENTS.md` + project scope root→cwd (closer-to-cwd
  overrides), 32 KiB default cap. Advisory, not enforcement. ([AGENTS.md][codex-agents]) **[H]**

---

## 6. Context-engineering patterns (vendor-neutral)

- **Definition.** Context engineering = "curating and maintaining the optimal set
  of tokens during inference" across many turns; the discipline is "the smallest
  set of high-signal tokens that maximize the likelihood of the desired outcome."
  Context is finite with diminishing returns. ([Anthropic][anthropic-ctx]) **[H]**
- **The Write / Select / Compress / Isolate taxonomy is LangChain's** (Lance
  Martin) synthesis, **not** Anthropic's own four words. ([LangChain][lc-ctx],
  [Lance Martin][rlm]) **[H]**
- **Compaction (compress).** Summarize a near-full window and reinitialize — keep
  architectural decisions / unresolved bugs / implementation details, drop redundant
  tool output. Lossy; "overly aggressive compaction can lose subtle but critical
  context." Manus's restorable compression is closer to lossless (drop a page body,
  keep the URL). ([Anthropic][anthropic-ctx], [Manus][manus]) **[H]**
- **Isolation vs fragmentation (the core debate).** Anthropic: sub-agents with clean
  windows explore in parallel, return distilled summaries — at up to ~15× token
  cost. Cognition (*Don't Build Multi-Agents*): sub-agents act on **conflicting
  implicit assumptions** (the Flappy-Bird-with-mismatched-art example); prefer a
  single-threaded linear agent + a dedicated compression model; *"share full agent
  traces, not just individual messages."* ([Anthropic][anthropic-ctx],
  [Cognition][cognition], [smol.ai][smol]) **[H]**
- **Offloading / external memory.** Notes persisted outside the window
  (`NOTES.md`/to-do), Manus's "file system as the ultimate context," Simon
  Willison's "context offloading" (`plan.md`). ([Anthropic][anthropic-ctx],
  [Manus][manus], [Willison][willison]) **[H]**
- **KV-cache stability (directly relevant to mid-stream injection).** A single-token
  prefix change invalidates cache from that token onward (a timestamp kills hit
  rate); cached input ≈ $0.30/MTok vs uncached ≈ $3.00/MTok (~**10×**); input:output
  ratio ~100:1. Make context **append-only**; mask tools, don't remove them.
  **Injecting/rewriting mid-stream is actively harmful** — invalidates cache *and*
  desyncs the model. ([Manus][manus]) **[H]**
- **"Snapshot/fork an agent's state" is not a validated model-level standard.** It
  is DIY transcript serialize/replay everywhere it appears, made cheap (not
  stateful) by transparent prefix/KV caching. ([Anthropic SDK issue #88][cl-sdk-issue]) **[H]**

---

## 7. OSS deterministic save / restore / fork

| Tool | Save | Restore | Fork (validated) | Mechanism |
|------|------|---------|------------------|-----------|
| **LangGraph** | ✅ checkpointers | ✅ thread_id/checkpoint_id | ✅ **first-class** (`update_state` → branching checkpoint) | versioned checkpoint tree per super-step |
| **Pydantic AI** | ✅ `*_json()` | ✅ `message_history=` | ✅ manual (reinject same log into N runs) | serialize message log + reinject |
| **OpenAI Agents SDK** | ✅ Session stores | ✅ auto per `session_id` | ⚠️ `AdvancedSQLiteSession` branching | serialize items to SQLite/Redis |
| **Letta / MemGPT** | ✅ `.af` export | ✅ `.af` import | ⚠️ via copy/checkpoint of `.af` | serialize whole agent (blocks+history) |
| **AutoGen** | ✅ `save_state` | ✅ `load_state` | ⚠️ duplicate the dict | serialize state dict to JSON |
| **MCP** | ❌ | ❌ | ❌ | tool/data-access protocol — **adjacent, not a snapshot** |

- **LangGraph is the gold standard.** Checkpointers (`MemorySaver`/`SqliteSaver`/
  `PostgresSaver`) snapshot graph state at every super-step, keyed by `thread_id`.
  `get_state_history()` lists past checkpoints; **`update_state(cfg, values=…)`**
  *"creates a new checkpoint that branches from the specified point — the original
  execution history remains intact"* (a genuine typed fork). **Determinism caveat:**
  replay/fork **re-executes** downstream nodes (LLM calls fire again) — context
  state is deterministic, the continuation isn't unless seeded/cached.
  ([persistence][lg-persist], [time-travel][lg-tt]) **[H]**
- **Pydantic AI:** `all_messages_json()` → restore via `ModelMessagesTypeAdapter` →
  feed `message_history=` into a new run = deterministic inject/fork. ([msg history][pyd]) **[H]**
- **Letta `.af`** serializes a whole stateful agent (system prompt, memory blocks,
  tools, full chat history with per-message `in_context` flag); archival passages
  not yet included. ([agent-file][letta]) **[H]**
- **MCP is NOT a context-snapshot mechanism** — JSON-RPC for exposing
  tools/resources/prompts; "stateful session" = the connection lifecycle, not
  durable agent-state. Complementary, not a substitute. ([spec][mcp]) **[H]**

**Invariant across all of them:** no magic window snapshot — serialize the ordered
message/state log, persist under a key (`thread_id`/`session_id`/`.af`/state dict),
replay or branch it.

---

## 8. Answering the three explicit asks

**(1) Deterministically view/extract a context window**
- Live: `/context` + `/memory` in-session.
- Out-of-band (the real answer): parse the session JSONL (`~/.claude/projects/...`
  or `~/.codex/sessions/...`) — complete, ordered, replayable. Caveat: it mirrors
  the conversation at high fidelity but isn't bit-identical to the model's input
  (un-billed system tokens; compaction rewrites history).

**(2) Copy/fork a built-up context to a sibling — most → least robust**
1. **Native fork** — `claude --resume <id> --fork-session` / `/branch`. Intended
   primitive; subscription-safe over ConPTY.
2. **Copy the JSONL** to a new `<session-id>.jsonl` under the same encoded-cwd, then
   `--resume`. Works because the transcript is source-of-truth; documented for
   cross-host portability, manual-fork use inferred — **test it**.
3. **Subagent** — when you want isolation + a distilled result, not a true clone.

**(3) Inject a primed starting context (not "read these files")**
1. **Transcript replay** — construct/restore a schema-correct `<session-id>.jsonl`
   (matching cwd), then `--resume`. The literal "here is your starting context"
   mechanism, but **schema-fragile** and not an official write API.
2. **Distill-and-reprompt (Anthropic's recommendation)** — capture
   analysis/decisions/diffs as *your* app state, pass into a fresh session's prompt
   (or CLAUDE.md / `--append-system-prompt`); docs call this *"often more robust
   than shipping transcript files around."*

---

## 9. Implications for this rebuild

A ConPTY-driven subscription "context engine" reduces to **three capabilities, all
at the transcript/file layer**:

1. **Read path** — parse session JSONL for deterministic inspection/visualization.
2. **Fork path** — `--fork-session` (native, safe) by default; JSONL-copy +
   `--resume` as the manual-clone escape hatch.
3. **Seed path** — prefer distill-and-reprompt (+ CLAUDE.md / `--append-system-prompt`)
   over hand-built JSONL replay (unversioned, fragile schema).

**Design constraints to honor:**
- **Append-only, stable prefix, never edit the middle** (KV-cache + model-sync).
- A **true full-transcript fork** sidesteps Cognition's fragmentation critique; a
  **fresh subagent + summary** is exactly what it warns about. Choose per intent:
  shared understanding → fork; bounded isolated subtask → subagent.
- The "snapshot the live window like a VM" fantasy is unavailable from anyone —
  context *is* the log. The log is fully in your hands (Claude) or one resume-command
  away (both), so everything asked for is buildable today, at the message-log layer.

---

## Sources

- Anthropic — Messages API (stateless / content blocks): [cl-msg], [cl-api]
- Anthropic — prompt caching: [cl-cache]
- Anthropic — context editing: [cl-ctxedit], [cl-ctxnews]
- Anthropic — memory tool: [cl-memory] · Files API: [cl-files] · token counting: [cl-tokens]
- Claude Code — sessions/fork: [cc-sessions] · context window/compaction/subagents: [cc-ctx], [cc-sub]
- Claude Code — Agent SDK sessions: [cc-sdk] · session storage: [cc-store] · stream-json issue: [cc-issue] · JSONL format (3rd-party): [cc-format]
- OpenAI — conversation state: [oai-state] · Conversations API: [oai-conv] · forking cookbook: [oai-cookbook]
- Codex CLI — features/resume: [codex-cli] · AGENTS.md: [codex-agents] · rollout discussion: [codex-disc] · resume guide: [codex-resume]
- Context engineering — Anthropic: [anthropic-ctx] · LangChain: [lc-ctx] · Lance Martin: [rlm] · Cognition: [cognition] · Manus: [manus] · Phil Schmid: [philschmid] · Simon Willison: [willison] · smol.ai: [smol]
- OSS — LangGraph persistence: [lg-persist] / time-travel: [lg-tt] · Pydantic AI: [pyd] · Letta agent-file: [letta] · OpenAI Agents SDK sessions: [oai-agents] · MCP: [mcp]
- Cross-impl fork-as-replay evidence: [codex-pr], [zed-pr], [agentsview], [cl-sdk-issue]

[cl-msg]: https://platform.claude.com/docs/en/build-with-claude/working-with-messages
[cl-api]: https://docs.anthropic.com/en/api/messages
[cl-cache]: https://docs.anthropic.com/en/docs/build-with-claude/prompt-caching
[cl-ctxedit]: https://docs.claude.com/en/docs/build-with-claude/context-editing
[cl-ctxnews]: https://www.anthropic.com/news/context-management
[cl-memory]: https://platform.claude.com/docs/en/agents-and-tools/tool-use/memory-tool
[cl-files]: https://platform.claude.com/docs/en/build-with-claude/files
[cl-tokens]: https://platform.claude.com/docs/en/build-with-claude/token-counting
[cc-sessions]: https://code.claude.com/docs/en/sessions
[cc-ctx]: https://code.claude.com/docs/en/context-window
[cc-sub]: https://code.claude.com/docs/en/sub-agents
[cc-sdk]: https://code.claude.com/docs/en/agent-sdk/sessions
[cc-store]: https://code.claude.com/docs/en/agent-sdk/session-storage
[cc-issue]: https://github.com/anthropics/claude-code/issues/24594
[cc-format]: https://databunny.medium.com/inside-claude-code-the-session-file-format-and-how-to-inspect-it-b9998e66d56b
[oai-state]: https://developers.openai.com/api/docs/guides/conversation-state
[oai-conv]: https://platform.openai.com/docs/api-reference/conversations/create
[oai-cookbook]: https://developers.openai.com/cookbook/examples/responses_api/responses_example
[codex-cli]: https://developers.openai.com/codex/cli/features
[codex-agents]: https://developers.openai.com/codex/guides/agents-md
[codex-disc]: https://github.com/openai/codex/discussions/3827
[codex-resume]: https://inventivehq.com/knowledge-base/openai/how-to-resume-sessions
[anthropic-ctx]: https://www.anthropic.com/engineering/effective-context-engineering-for-ai-agents
[lc-ctx]: https://www.langchain.com/blog/context-engineering-for-agents
[rlm]: https://rlancemartin.github.io/2025/06/23/context_engineering/
[cognition]: https://cognition.ai/blog/dont-build-multi-agents
[manus]: https://manus.im/blog/Context-Engineering-for-AI-Agents-Lessons-from-Building-Manus
[philschmid]: https://www.philschmid.de/context-engineering
[willison]: https://simonwillison.net/tags/context-engineering/
[smol]: https://news.smol.ai/issues/25-06-13-cognition-vs-anthropic
[lg-persist]: https://docs.langchain.com/oss/python/langgraph/persistence
[lg-tt]: https://docs.langchain.com/oss/python/langgraph/use-time-travel
[pyd]: https://github.com/pydantic/pydantic-ai/blob/main/docs/message-history.md
[letta]: https://github.com/letta-ai/agent-file
[oai-agents]: https://openai.github.io/openai-agents-python/sessions/
[mcp]: https://modelcontextprotocol.io/specification/2025-11-25/server/tools
[codex-pr]: https://github.com/openai/codex/pull/18023
[zed-pr]: https://github.com/zed-industries/zed/pull/27967
[agentsview]: https://github.com/wesm/agentsview/issues/107
[cl-sdk-issue]: https://github.com/anthropics/claude-agent-sdk-typescript/issues/88
