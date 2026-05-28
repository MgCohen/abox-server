# C# Orchestrator Rewrite — Planning Handover

> **Purpose**: this document captures the full context, design conversation,
> reference audit, and architecture decisions that led to the proposal of
> rewriting our JS orchestrator (`remote-agents/orchestrator/`) in C#/.NET 10.
> A receiving planning agent should be able to read this end-to-end and produce
> a concrete implementation plan without needing the prior chat transcript.
>
> **Status as of writing**: design proposal accepted in principle; concrete
> planning not yet done. PTY-on-.NET load-bearing risk is empirically resolved
> (smoke test passed). No C# orchestrator code written yet.

---

## 1. Context — what exists today

### 1.1 The current JS orchestrator

Lives at `C:\Unity\remote-unity-agents\remote-agents\orchestrator\`. Branch:
`phase-a/local-validation`. Working. Pushed.

**What it is**: a Node 20+ library + CLI that drives `claude` and `codex` CLIs
against project repos. Designed to keep both on **subscription billing** (Max
quota for Claude, ChatGPT subscription for Codex) instead of per-token API
billing.

**Core trick**: `claude` checks `isatty(stdin) && isatty(stdout)` on startup.
TTY-both → subscription. Pipe-either → API (Agent SDK Credit pool post
2026-06-15). We spawn `claude` inside `node-pty` (Microsoft ConPTY on Windows,
forkpty on Unix) so it sees real TTYs on both sides. Codex doesn't need this —
`codex exec` is officially supported on ChatGPT subs since April 2026; plain
`child_process.spawn` is fine.

**Empirically verified**: see `research/pty-smoke-result.md` and
`~/.claude/projects/.../*.jsonl` showing `apiProvider:"firstParty"`,
`subscriptionType:"max"`, `entrypoint:"claude-desktop"`. A full end-to-end run
(`full-review` flow, JSDoc addition task) shipped a real commit (`14b5cc8`).

### 1.2 File map of the JS orchestrator

```
remote-agents/orchestrator/
├── bin/agents.js                       # CLI: `agents run <flow> ...`
├── src/
│   ├── index.js                        # public library surface
│   ├── providers/
│   │   ├── claudeProvider.js           # PTY-driven, ~150 LoC
│   │   └── codexProvider.js            # spawn + `-o` capture, ~150 LoC
│   └── lib/
│       ├── ansi.js                     # stripAnsi + sleep
│       ├── fsdiff.js                   # snapshotFiles / diffFiles
│       ├── git.js                      # gitDiff, commit, push, isDirty, ...
│       ├── projects.js                 # resolveProject(name) via projects.json
│       ├── requireSubscription.js      # throws if API keys are set
│       ├── runCommand.js               # async child_process wrapper
│       └── session.js                  # sessions/<iso>-<slug>/ writer
├── flows/                              # user-authored flow scripts
│   ├── claude-only.mjs
│   ├── claude-validate.mjs             # validate + fix loop
│   └── full-review.mjs                 # Claude → validate → Codex → commit
├── validation/
│   └── orchestrator.mjs                # per-project validator (node --check)
├── sessions/                           # gitignored transcripts
├── projects.json                       # short-name → absolute-path map
└── package.json                        # node-pty dep
```

The split is deliberate: **library code in `src/`** (stable contract),
**user-authored scripts in `flows/` and `validation/`** (edit freely). The
library imposes no control flow — flows are hand-written `for`/`while`/`if`.

### 1.3 Documentation already written

- `remote-agents/orchestrator/docs/architecture.md` — internals (PTY trick,
  provider contract, session format, lifecycle diagram).
- `remote-agents/orchestrator/docs/usage.md` — practical guide (setup, writing
  a flow, writing a validator, recipes, troubleshooting).

Both should be ported / replaced for the C# rewrite.

### 1.4 Other repo context relevant to this rewrite

- `PLANS/unity-agent-infrastructure.md` — the larger plan this orchestrator
  serves. Local-validation phase (Phase A), then Hetzner VM with native Unity
  Hub install for headless Unity work.
- `research/` — empirical investigation logs.
- The orchestrator drives Unity and non-Unity projects. Unity projects sit at
  `C:\Unity\<ProjectName>` (Card Framework, Scaffold, Gear-Engine, plus the
  meta-project `remote-unity-agents` itself).

---

## 2. The design conversation — what triggered this rewrite

### 2.1 Original concern (the user raised)

The JS orchestrator has two provider modules (`claudeProvider`,
`codexProvider`) and exposes them as flat functions (`runClaude`, `runCodex`).
Flow scripts call those directly. Implicit problem: **there's no concept of a
named, reusable "agent"** — a planner that's "Opus 4.7 + planning system
prompt", a documenter that's "Haiku + documentation system prompt", a
researcher that's "gpt-5.3-codex + research system prompt". Today the user
would have to repeat all that configuration at every call site.

What the user wants:
- A **provider layer** that handles talking to a CLI: model, transport,
  back-and-forth communication, subscription vs. API decisions.
- An **agent layer** on top: where named definitions live (system prompts,
  preferred model, role/options), and that knows how to pass those down to a
  provider.
- The ability to define agents once and reuse them across flows.

### 2.2 Why this matters for real workflows

> "In one workflow I might need a Claude agent that runs Opus 4.7 for planning,
> and then I might need another agent that runs Haiku for documentation. In
> another workflow I might need codex 5.3 for web research. They all have
> system prompts or preset instructions specific to their role."

The provider layer alone can't express that. The user wants the two-layer
split made explicit.

---

## 3. Reference audit — what other frameworks taught us

All five references live as MD files in `research/`. The
question we asked of each: **do they have the provider/agent split, and if so,
what shape does the agent layer take?**

### 3.1 Flue — closest verbatim precedent

- Repo: https://github.com/withastro/flue
- Research file: `research/flue.md`
- **Tagline**: `Agent = Model + Harness`. Four-layer stack:
  ```
  Model (tokens, tools, prompts)
    → Harness (skills, memory, sessions)
    → Sandbox (bash, security)
    → Filesystem
  ```
- **Model** layer = our provider layer. **Harness** layer = our agent layer.
- Agent logic lives in **Markdown** files (`AGENTS.md`, skills).
- We don't use Flue itself because it sits on pi.dev (BYOK API path; would
  burn our subscription advantage). But the architecture is exactly what we
  want.

### 3.2 Sandcastle — confirms the provider shape; lacks persistence

- Repo: https://github.com/mattpocock/sandcastle
- Research file: `research/sandcastle.md`
- Has **pluggable "agent providers"** (Claude Code, Codex CLI, Cursor, Pi,
  OpenCode, Copilot) and pluggable sandbox providers.
- Actual API shape (from their docs):
  ```ts
  await run({
    agent: claudeCode("claude-opus-4-7"),
    sandbox: docker(),
    promptFile: ".sandcastle/prompt.md",
  });
  ```
- Each agent factory (`claudeCode("...")`, `codex("...")`, etc.) returns a
  normalized handle. **But the agent is defined inline per call** — there's no
  persisted/named registry. System prompt is a `promptFile` reference, not an
  inline string.
- Initial reading: "no agent layer." Corrected reading: **has the two layers
  but doesn't persist the agent layer.** That's a deliberate fit for their
  parallel-worktree use case, not ours.

### 3.3 Pydantic AI / Mastra / Microsoft Agent Framework

All three have an explicit `Agent` class on top of provider plumbing. Closest
to what the user described:

```python
# Pydantic AI
agent = Agent(
  model='anthropic:claude-opus-4-7',
  system_prompt='You are a software architect...',
  tools=[...],
)
result = await agent.run('your prompt')
```

```ts
// Mastra
const planner = new Agent({
  name: 'planner',
  instructions: 'You are a software architect...',
  model: anthropic('claude-opus-4-7'),
});
```

User-facing entry is `agent.run(prompt)`, never `provider.run(prompt)`.
Provider is a *parameter* of the agent, not the API surface.

### 3.4 LiteLLM — the dumb-provider precedent

- Repo: https://github.com/BerriAI/litellm
- Research file: `research/litellm.md`
- Pure provider-normalization layer; **no agent concept at all.**
  ```python
  litellm.completion(model="anthropic/claude-opus-4-7", messages=[...])
  ```
- Lesson worth taking: **provider layer should be as dumb as possible.** One
  verb, normalized inputs (model + messages), normalized outputs. All
  personality (system prompts, role, presets) belongs above.
- We won't use LiteLLM as a primary path (it's API-key only — no subscription
  routing). But it's our **fallback layer**: if the PTY trick ever breaks or
  Max quota is exhausted, the same provider interface can route to LiteLLM
  HTTP instead. Documented in `alternatives-considered.md`.

### 3.5 Claude Agent SDK — the cautionary tale

- The Anthropic-official SDK has two layers (Agent + Model). But the agent
  abstraction is **Claude-shaped** — Anthropic message format, Anthropic tool
  schemas, Anthropic system-prompt semantics.
- Lesson: **don't let one provider's mental model leak into the agent
  abstraction.** Specifically:
  - Don't expose Claude's `permissionMode` at the agent level.
  - Don't expose Codex's `sandbox` at the agent level.
  - System prompt is a single string at the agent level. Provider translates.

### 3.6 Synthesis — what the references agree on

| Framework         | Has agent layer?   | Persisted?              | Prompt mechanism             |
|-------------------|--------------------|-------------------------|------------------------------|
| LiteLLM           | No                 | —                       | messages array per call      |
| Sandcastle        | Yes, inline only   | No                      | `promptFile` reference       |
| Flue              | Yes                | Yes — Markdown files    | `AGENTS.md`                  |
| Pydantic AI       | Yes                | Yes — `Agent` instance  | `system_prompt=` field       |
| Mastra            | Yes                | Yes — `new Agent({...})`| `instructions=` field        |
| Claude Agent SDK  | Yes                | Yes (Claude-shaped)     | Native `system=` parameter   |

The consensus shape we adopted (informed by all six):

```
┌────────────────────────────────────────────────────────────┐
│ AGENT LAYER (named, reusable, persisted)                  │
│   - name, systemPrompt, model preference                   │
│   - provider-specific options (pass-through)               │
│   - one verb: agent.run({prompt, sessionId, projectDir})   │
├────────────────────────────────────────────────────────────┤
│ PROVIDER LAYER (dumb, normalized)                          │
│   - knows how to talk to one CLI (PTY or spawn)            │
│   - inputs: model + prompt + systemPrompt + transport      │
│     details (sessionId, cwd, options)                      │
│   - returns: normalized {text, sessionId, exitCode, raw}   │
└────────────────────────────────────────────────────────────┘
```

---

## 4. The agreed two-layer architecture

These are the decisions reached during the conversation, ordered by
significance:

### 4.1 Provider layer

- **One factory per CLI**: `claude(model, opts)`, `codex(model, opts)`. Each
  returns a normalized handle with one verb: `.run(...)`. Pattern borrowed
  from Sandcastle.
- **Input vocabulary**: `prompt`, `systemPrompt`, `sessionId`, `projectDir`,
  `options`. Everything else (Claude's `permissionMode`, Codex's `sandbox`,
  PTY timings) lives inside `options` as provider-specific pass-through.
- **Return shape**: `{text, sessionId, exitCode, rawOutput}` for both. Codex
  also includes `stderr` and `timedOut`.
- **Providers know about transport, model, and CLI flags. They know nothing
  about agents, roles, named presets, or memory.**

### 4.2 Agent layer

- An agent is `{name, provider, systemPrompt, options}` plus a `.run()`
  method that delegates to the provider.
- **Agents are persisted as code modules**, one per file. Consistent with how
  flows and validators already work.
- **`role` was considered and dropped.** Initial proposal had
  `role: 'editor' | 'reviewer'` as a normalized vocabulary that expanded to
  per-provider safety flags (`permissionMode`/`sandbox`). User correctly
  pushed back: it's a 4-line lookup table, no logging or behavioral value,
  hides what's happening from readers. **Agents now set provider-specific
  options directly** (`{ permissionMode: 'plan' }` for Claude reviewer,
  `{ sandbox: 'read-only' }` for Codex reviewer). Provider-spec leakage is
  acceptable because each agent already picks a specific provider.

### 4.3 System prompt strategy

- **At the agent level: one string** (or loaded from a sibling `.md` file —
  no special mechanism, just file IO).
- **Per-provider injection**:
  - Claude: native `--append-system-prompt <text>` flag.
  - Codex: prepend to user prompt (`${systemPrompt}\n\n---\n\n${prompt}`).
    Codex has no `--system-prompt` flag. Could write a temp `AGENTS.md`
    instead but prepend is simpler and works.
- **Honest about the divergence**: we expose one interface, not one
  mechanism.

### 4.4 Memory

- **No memory primitive in the initial design.** Skip entirely.
- **Why this is fine**:
  - `claude` auto-reads `CLAUDE.md` from the project (walks up directories).
  - `codex` auto-reads `AGENTS.md` similarly.
  - "Memory across runs as context" is already happening at the CLI level
    for free.
- **For per-flow state** (decisions, plans, notes), use the **same pattern
  as validators**: hand-written file IO inline in the flow script.
  ```csharp
  var result = await planner.RunAsync(...);
  File.AppendAllText(memoryFile, $"\n## {DateTimeOffset.UtcNow}\n{result.Text}\n");
  ```
- **If a hook pattern emerges across 3+ flows**, lift it into the agent
  layer as `BeforeRun`/`AfterRun` hooks. Not before. Same principle that
  worked for validators: the agent learns from the flow, not the other way
  around.

### 4.5 Backward compatibility / migration

- **Run JS and C# orchestrators in parallel** during the rewrite. Not a flag
  day.
- The JS orchestrator stays working on `phase-a/local-validation` branch.
- C# rewrite goes in a new `remote-agents-dotnet/` directory (sibling of
  `remote-agents/`).
- Feature parity is reached file-by-file. Existing flows get re-implemented
  in C#; old ones keep running in JS until replaced.

---

## 5. C# validation — the smoke test that flipped the recommendation

The single load-bearing risk for any C# rewrite: **can .NET drive Claude's
TUI through ConPTY reliably, the way `node-pty` does?** Everything else is
conventional .NET work.

A standalone smoke test was built **outside this repo** at
`C:\Unity\dotnet-pty-smoke\`:

```
C:\Unity\dotnet-pty-smoke\
├── PtySmoke.csproj          # .NET 10 console app
├── Program.cs               # 3-stage smoke
├── pty-stage1.log           # claude --version PTY timeline
├── pty-stage2.log           # claude TUI session PTY timeline (reference)
├── pty-stage3-auth.log      # auth status JSON post-test
└── stage2-work/             # empty workdir for the test claude session
```

### 5.1 Dependencies validated

- **`Microsoft.Windows.Console.ConPTY` 1.24** — owned by Microsoft.Terminal,
  ships native `conpty.dll`.
- **`Porta.Pty` 1.0.7** — managed wrapper that P/Invokes
  `CreatePseudoConsole`/`ResizePseudoConsole`/`ClosePseudoConsole` via
  `Vanara.PInvoke.Kernel32`. 34K downloads, cross-platform (Win/Linux/macOS).

### 5.2 Test stages and results

**Stage 1 (sanity) — PASS.** Spawned `cmd.exe` via Porta.Pty, sent
`claude --version\r`, sent `exit\r`, captured output. Regex matched
`\d+\.\d+\.\d+`. Clean exit 0.

**Stage 2 (full TUI) — PASS (after one bug fix).** Spawned `cmd.exe`, sent
`claude --permission-mode acceptEdits --session-id <our-uuid> "Reply with
exactly the word PONG"`, handled trust dialog, idle-waited, sent `/exit\r`
then `exit\r`. Final log:

```
[stage2] pty spawned, pid=45336
[stage2] exited=True exitCode=0
[stage2] captured 6072 chars
[stage2] sawClaudeUi=True sawPong=True
[stage2] claude session file present: True
```

Confirms:
- ConPTY drives Claude TUI start-to-finish (alt-screen, cursor, repaints,
  color).
- `--session-id <uuid>` honored — Claude wrote
  `~/.claude/projects/.../{our-uuid}.jsonl`.
- Prompt was processed, model responded with PONG.
- Clean exit code 0.
- Resume hint emitted: `claude --resume 0b1f90ea-fa90-4554-86e5-606bdd2518e7`.

**Stage 3 (billing verification) — PASS.** Post-test `claude auth status`
returns subscription-path JSON with `claude.ai` / `subscription` / `Max`
markers. **No billing-mode contamination from PTY-driving.**

**Total runtime**: 18.2 seconds for all 3 stages. Stage 2 alone ~10s —
competitive with the JS orchestrator's `claudeProvider` (~12–15s for
comparable work).

### 5.3 Carry-forward findings from the smoke

Two issues to bake into the C# base class from day one:

1. **`Porta.Pty.IPtyConnection.ExitCode` throws if the process hasn't
   exited.** Doesn't check `HasExited`. Need a wrapper:
   ```csharp
   public static int? ExitCodeOrNull(this IPtyConnection pty)
       => pty.HasExited ? pty.ExitCode : null;
   ```

2. **Trust-dialog regex needs Claude v2.1.x's exact text.** The smoke's
   first attempt used `"Do you trust"` / `"Trust this folder"`, which missed
   the actual v2.1.x wording. The current JS detector already handles it
   correctly (verified at `claudeProvider.js:135`):
   ```js
   plain.includes('trust this folder') || plain.includes('Is this a project you')
   ```
   Port these exact two substrings to C#. Don't tighten.

A third finding worth noting:

3. **Claude emits a useful resume hint on clean exit**:
   `Resume this session with: claude --resume <uuid>`. Could be parsed as a
   secondary session-id verification path (we already pass `--session-id`
   ourselves, so this is belt-and-suspenders, but cheap to grab).

### 5.4 Verdict

| Question                                           | Answer                                          |
|----------------------------------------------------|-------------------------------------------------|
| Can C#/.NET drive ConPTY reliably?                 | Yes                                             |
| Does it work with Claude's TUI specifically?       | Yes — 6072 chars, PONG roundtrip                |
| Does `--session-id <uuid>` passthrough work?       | Yes                                             |
| Does PTY-driven claude still bill subscription?    | Yes — Stage 3 confirmed                         |
| Does running the smoke disturb the JS orchestrator?| No — fully isolated under `C:\Unity\dotnet-pty-smoke\` |
| Is a published managed PTY lib available?          | Yes — Porta.Pty                                 |

**Load-bearing risk is resolved.** C# rewrite is green-lit on that front.

---

## 6. Proposed C# architecture

These are sketches, not final designs. The receiving planner should refine.

### 6.1 Why C# over TypeScript (for this specific codebase)

- **`sealed` lifecycle**: a base `Agent` class with `sealed Task<AgentResult>
  RunAsync(...)` forces subclasses through the event-emission path at
  compile time. TS has no equivalent.
- **Discriminated event hierarchy with exhaustiveness**: `abstract record
  AgentEvent` + `sealed record` cases + switch-expression exhaustiveness is
  *real* in C#. TS discriminated unions exist but exhaustiveness is weaker
  (no compiler error when adding a variant and missing a switch arm).
- **Roslyn refactoring** beats TS tooling for rename/extract operations.
- **.NET 10 file-based programs** (`dotnet run flow.cs`, shipped Nov 2025)
  match `tsx`'s edit-save-run iteration speed. No `dotnet build` step
  needed for flows.
- **Unity-familiar mental model** for the user (every Unity project is C#).
- **Direct type-sharing with a future MAUI Blazor Hybrid UI** if that
  becomes the front-end choice (see §8).

What C# *doesn't* buy us (don't oversell):
- For ~500 LoC of provider+agent code, C# and TS-with-strict-mode are
  roughly equivalent in basic safety. The structural payoff is specifically
  the sealed lifecycle and exhaustive switch — not "C# is stronger than TS
  in general."
- Unity projects don't share code with the orchestrator; the C# mental
  model continuity is real but doesn't translate to direct code reuse.

What C# *does* cost:
- ~3–5 days of glue rewrites (providers, git helpers, session writer).
- Smaller ecosystem in the CLI-orchestration space — we can't directly
  crib code from Sandcastle (TS), Flue (TS), OpenClaw (JS), Codeman (JS),
  Mastra (TS), Pydantic AI (Python), or any other prior art.
- Maintaining a wrapper around `Porta.Pty.ExitCode` and other minor gaps
  vs. node-pty's maturity.

### 6.2 Proposed project layout

```
remote-agents-dotnet/
├── RemoteAgents.sln
├── RemoteAgents/                       # the library
│   ├── RemoteAgents.csproj             # net10.0
│   ├── Agents/
│   │   ├── Agent.cs                    # abstract base, sealed RunAsync
│   │   ├── ClaudeAgent.cs              # sealed, owns Porta.Pty driving
│   │   ├── CodexAgent.cs               # sealed, owns Process driving
│   │   ├── AgentRunRequest.cs          # record
│   │   └── AgentResult.cs              # record
│   ├── Events/
│   │   ├── AgentEvent.cs               # abstract record + sealed cases
│   │   ├── IEventSink.cs               # interface
│   │   ├── JsonlSink.cs                # transcript.jsonl writer
│   │   ├── ConsoleSink.cs              # stdout writer
│   │   └── CompositeSink.cs            # fan-out
│   ├── Sessions/
│   │   ├── Session.cs                  # sessions/<iso>-<slug>/ folder
│   │   └── SessionMeta.cs              # meta.json shape
│   ├── Git/
│   │   └── GitOps.cs                   # Diff, Commit, Push, IsDirty, ...
│   ├── Validation/
│   │   ├── IValidator.cs               # one interface
│   │   └── ValidationResult.cs         # record
│   ├── Projects/
│   │   ├── ProjectRegistry.cs          # projects.json loader
│   │   └── projects.json
│   ├── Subscription/
│   │   └── SubscriptionGuard.cs        # throws if API keys set
│   └── Pty/
│       └── PtyExtensions.cs            # ExitCodeOrNull, etc.
├── flows/                              # .NET 10 file-based programs
│   ├── claude-only.cs                  # #:project ../RemoteAgents/RemoteAgents.csproj
│   ├── claude-validate.cs
│   └── full-review.cs
├── agents/                             # named agent registrations
│   ├── Planner.cs                      # static factory
│   ├── Documenter.cs
│   ├── Researcher.cs
│   └── prompts/                        # optional sidecar prompts
│       ├── planner.md
│       └── researcher.md
├── validation/                         # user-authored validators
│   ├── OrchestratorValidator.cs        # implements IValidator
│   ├── CardFrameworkValidator.cs       # Unity batch-mode compile
│   └── ScaffoldValidator.cs
├── sessions/                           # gitignored transcripts
└── README.md
```

### 6.3 Core type sketches

**Agent base class (template method pattern with sealed lifecycle):**

```csharp
public abstract class Agent
{
    public required string Name { get; init; }
    public required string Model { get; init; }
    public string? SystemPrompt { get; init; }
    public IReadOnlyDictionary<string, object>? Options { get; init; }

    // Sealed — subclasses cannot bypass the lifecycle.
    public sealed async Task<AgentResult> RunAsync(
        AgentRunRequest req,
        IEventSink sink,
        CancellationToken ct = default)
    {
        await sink.EmitAsync(new AgentEvent.Started(Name, req.Prompt, req.SessionId), ct);
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await ExecuteAsync(req, sink, ct);
            await sink.EmitAsync(new AgentEvent.Completed(Name, result, sw.Elapsed), ct);
            return result;
        }
        catch (Exception ex)
        {
            await sink.EmitAsync(new AgentEvent.Failed(Name, ex.Message, sw.Elapsed), ct);
            throw;
        }
    }

    // Provider-specific implementation — the only subclass override point.
    protected abstract Task<AgentResult> ExecuteAsync(
        AgentRunRequest req,
        IEventSink sink,
        CancellationToken ct);
}
```

**Discriminated event hierarchy:**

```csharp
public abstract record AgentEvent(string AgentName)
{
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;

    public sealed record Started(string AgentName, string Prompt, string? SessionId)
        : AgentEvent(AgentName);

    public sealed record StreamChunk(string AgentName, string Text)
        : AgentEvent(AgentName);

    public sealed record DialogDismissed(string AgentName, string DialogKind)
        : AgentEvent(AgentName);

    public sealed record Completed(string AgentName, AgentResult Result, TimeSpan Duration)
        : AgentEvent(AgentName);

    public sealed record Failed(string AgentName, string ErrorMessage, TimeSpan Duration)
        : AgentEvent(AgentName);
}
```

**Exhaustive switch (for replay/UI consumption):**

```csharp
string Format(AgentEvent e) => e switch
{
    AgentEvent.Started s         => $"[{s.AgentName}] start: {s.Prompt}",
    AgentEvent.StreamChunk c     => c.Text,
    AgentEvent.DialogDismissed d => $"[{d.AgentName}] dialog: {d.DialogKind}",
    AgentEvent.Completed done    => $"[{done.AgentName}] done in {done.Duration.TotalSeconds:F1}s",
    AgentEvent.Failed fail       => $"[{fail.AgentName}] FAIL: {fail.ErrorMessage}",
    _                            => throw new UnreachableException(),
};
```

Adding a new event variant later → every switch on `AgentEvent` becomes a
compiler warning. That's the safety property TS can't enforce cleanly.

**Concrete provider implementations:**

```csharp
public sealed class ClaudeAgent : Agent
{
    protected override async Task<AgentResult> ExecuteAsync(
        AgentRunRequest req, IEventSink sink, CancellationToken ct)
    {
        // 1. Build claude args:
        //      --session-id <new uuid>  OR  --resume <req.SessionId>
        //      --permission-mode <Options["permissionMode"] ?? "acceptEdits">
        //      --append-system-prompt <SystemPrompt>  (if set)
        //      --model <Model>
        // 2. Spawn via Porta.Pty (cmd.exe /c claude on Windows).
        // 3. Detect & dismiss startup dialog (trust vs. bypass-warning).
        //    Emit AgentEvent.DialogDismissed.
        // 4. Type prompt, send \r, idle-wait for completion.
        //    Stream chunks via AgentEvent.StreamChunk.
        // 5. Send /exit\r, wait for clean exit.
        // 6. Return AgentResult { Text, SessionId, ExitCode, RawOutput }.
        // 
        // Uses PtyExtensions.ExitCodeOrNull to avoid the Porta.Pty bug.
        // Unsets ANTHROPIC_API_KEY / CLAUDE_API_KEY in spawned env.
    }
}

public sealed class CodexAgent : Agent
{
    protected override async Task<AgentResult> ExecuteAsync(
        AgentRunRequest req, IEventSink sink, CancellationToken ct)
    {
        // System prompt: prepend to req.Prompt (Codex has no native flag).
        // Spawn via System.Diagnostics.Process (cmd.exe /c codex on Windows):
        //   codex exec [resume <SessionId>] --cd <ProjectDir>
        //     -o <tmpfile> --sandbox <Options["sandbox"] ?? "workspace-write">
        //     --dangerously-bypass-approvals-and-sandbox --json --model <Model> -
        // Pipe prompt via stdin.
        // Parse JSONL events from stdout to extract thread_id / session_id.
        // Read final message from tmpfile.
    }
}
```

**Validator interface (typed contract, unlike the JS duck-typed shape):**

```csharp
public interface IValidator
{
    Task<ValidationResult> ValidateAsync(
        string projectDir,
        AgentResult? lastAgentResult,
        CancellationToken ct);
}

public sealed record ValidationResult(
    bool Ok,
    string Summary,
    string Errors);
```

**Agent registration (one factory per named role):**

```csharp
// agents/Planner.cs
public static class Planner
{
    public static Agent Create() => new ClaudeAgent
    {
        Name = "planner",
        Model = "claude-opus-4-7",
        SystemPrompt = File.ReadAllText("agents/prompts/planner.md"),
        Options = new Dictionary<string, object>
        {
            ["permissionMode"] = "acceptEdits",
        },
    };
}
```

**Flow file (.NET 10 file-based program):**

```csharp
// flows/full-review.cs
#:project ../RemoteAgents/RemoteAgents.csproj

using RemoteAgents.Agents;
using RemoteAgents.Events;
using RemoteAgents.Sessions;
using RemoteAgents.Git;
using RemoteAgents.Subscription;

SubscriptionGuard.ThrowIfApiKeysSet();

var (projectName, userPrompt, shouldPush) = ParseArgs(args);
var projectDir = ProjectRegistry.Resolve(projectName);

if (await GitOps.IsDirtyAsync(projectDir))
{
    Console.Error.WriteLine("Working tree is dirty. Commit or stash first.");
    return 2;
}

await using var session = Session.Start(flowName: "full-review", projectName, projectDir, userPrompt);
var sink = new CompositeSink(new ConsoleSink(), new JsonlSink(session.TranscriptFile));

var planner = Planner.Create();
var reviewer = Reviewer.Create();

var result = await planner.RunAsync(
    new AgentRunRequest(userPrompt, SessionId: null, projectDir), sink);

// validate + fix loop
for (int attempt = 1; attempt <= 3; attempt++)
{
    var v = await new OrchestratorValidator().ValidateAsync(projectDir, result, default);
    if (v.Ok) break;
    if (attempt == 3) { Console.Error.WriteLine("validation failed"); return 2; }
    result = await planner.RunAsync(
        new AgentRunRequest(
            $"Validation failed. Address these issues:\n\n{v.Errors}",
            SessionId: result.SessionId,
            projectDir),
        sink);
}

// codex review
var diff = await GitOps.DiffAsync(projectDir);
if (string.IsNullOrWhiteSpace(diff))
{
    Console.WriteLine("no changes");
    return 0;
}
var reviewPrompt = BuildReviewPrompt(userPrompt, diff);
var review = await reviewer.RunAsync(
    new AgentRunRequest(reviewPrompt, SessionId: null, projectDir), sink);

// commit + optional push
await GitOps.CommitAsync(projectDir, diff.Files, BuildCommitMessage(userPrompt, review.Text));
if (shouldPush) await GitOps.PushAsync(projectDir, await GitOps.CurrentBranchAsync(projectDir));
```

Iteration speed: `dotnet run flows/full-review.cs <project> "<prompt>"`.
Edit, save, run. Equivalent to `node flows/full-review.mjs ...`.

### 6.4 What the rewrite explicitly does *not* add

- **No tool framework.** Validators and shell-outs use `Process.Start` /
  `runCommand` equivalent. No Anthropic-SDK-style tool definitions.
- **No memory primitive.** See §4.4.
- **No agent-calls-agent composition.** Composition is the flow's job.
- **No provider fallback.** Flow-level policy.
- **No Markdown-driven agents** (Flue's pattern). Agent registrations are C#
  static factories; prompts may live in sidecar `.md` files loaded via
  `File.ReadAllText`.
- **No multi-agent orchestration runtime** (Mastra/Agno style). The
  orchestrator stays a CLI + library, not a server.

---

## 7. Migration plan (rough)

A receiving planner should produce a detailed version of this. Outline:

1. **Scaffold `remote-agents-dotnet/`** alongside `remote-agents/`. Both
   coexist; nothing breaks.
2. **Port `Porta.Pty` smoke test in** as `RemoteAgents.Pty/SmokeProgram.cs`
   (or keep at `C:\Unity\dotnet-pty-smoke\` as a reference artifact and
   delete later).
3. **Build out core types** (`Agent`, `AgentEvent`, `IEventSink`,
   `AgentResult`, `AgentRunRequest`, `IValidator`, `Session`).
4. **Implement `ClaudeAgent`** mirroring `claudeProvider.js`. Carry forward
   the trust-dialog substrings and `ExitCodeOrNull` wrapper from §5.3.
5. **Implement `CodexAgent`** mirroring `codexProvider.js`.
6. **Implement git ops, session writer, subscription guard, project
   registry.** Direct ports.
7. **First flow: `flows/claude-only.cs`.** Parity with JS `claude-only.mjs`.
   Smoke test the full pipeline.
8. **Port the orchestrator self-validator** (`node --check` walker becomes
   a C# compile check — `dotnet build --no-incremental` or a Roslyn
   syntax-only parse).
9. **Second flow: `flows/claude-validate.cs`.**
10. **Third flow: `flows/full-review.cs`.** End-to-end with Codex review,
    commit, optional push. Compare against the JS run that produced commit
    `14b5cc8` as a baseline.
11. **Define first three real agents**: Planner, Documenter, Researcher.
    System prompts as sidecar `.md` files.
12. **Documentation**: port `architecture.md` and `usage.md` from the JS
    orchestrator. Update for C# specifics.
13. **CLI shim**: `agents-dotnet` (or similar) — equivalent to
    `bin/agents.js`. Lists flows, runs them via `dotnet run`.
14. **Validate one real Unity-project use case** end-to-end (e.g. against
    `card-framework`) before retiring the JS orchestrator.

Estimated effort, assuming familiarity with .NET: **3–5 working days** for
steps 1–10, plus another 2–3 for agent registrations, validators, and one
real-project shakedown. Conservative total: **2 weeks** including
documentation and at least one rewrite-after-learning iteration.

---

## 8. Open decisions for the planner to resolve

### 8.1 Tactical

- **Flow file format**: `.NET 10 file-based programs` (recommended — matches
  JS iteration speed) vs. a single console app that takes `--flow <name>`
  vs. one .csproj per flow. The first option requires `#:project` directive
  per file but is cleanest.
- **Validator API**: typed `IValidator` interface (recommended) vs.
  duck-typed `record ValidationResult` returned from any static method. The
  interface is more ceremony but gives refactor safety.
- **Event sink shape**: `IEventSink` with `Task EmitAsync` (recommended for
  async file IO) vs. `Action<AgentEvent>` (simpler but blocks file writes).
- **CancellationToken propagation**: present in every `ExecuteAsync` and
  `EmitAsync` signature (recommended) or omitted to keep signatures lean.
  Lean is fine for v1; can add later.
- **Subprocess transport on Windows**: spawn `cmd.exe /c claude` (matches
  JS) or `claude.cmd` directly (cleaner; needs verification that PATH
  resolution works under `Porta.Pty`).

### 8.2 Strategic — the UI direction question

The conversation that produced the smoke test continued into UI architecture.
**No UI code was written** but a tentative direction emerged:

- **MAUI Blazor Hybrid** (preferred): one codebase, three deliverables
  (Windows app, Android app, web). Direct `ProjectReference` from the UI
  project to `RemoteAgents.csproj`. Pass `AgentEvent` instances directly to
  the UI layer — no IPC, no OpenAPI codegen, no serialization boundary.
- **Tauri 2 + React** (runner-up): web-first UI, smaller bundle, but
  requires the orchestrator to expose an HTTP/JSON-RPC surface with
  OpenAPI codegen for type-sharing.

**This decision is structural** because it determines whether the
orchestrator's public API needs OpenAPI codegen tooling. MAUI lets the API
stay in-process; Tauri forces a network boundary.

The receiving planner should either:
- (a) Defer the UI decision and produce a plan that's UI-agnostic (the
  orchestrator library stays in-process; whichever UI is picked can attach
  later), or
- (b) Get the UI direction confirmed from the user before scoping, since
  MAUI's type-sharing thesis is a meaningful pull on the orchestrator's
  public types.

Recommendation: **(a)**. Build the orchestrator with no UI assumptions.
Library is `RemoteAgents.csproj`; consumer is `flows/*.cs` initially; UI
attaches later via `ProjectReference` (MAUI) or HTTP wrapper (Tauri).

---

## 9. State at handover

### 9.1 What exists

- **JS orchestrator** at `remote-agents/orchestrator/` — working, tested,
  committed, pushed (`phase-a/local-validation` branch, latest commit
  `4dbf95b`).
- **Smoke test artifact** at `C:\Unity\dotnet-pty-smoke\` — outside the
  repo. Three log files (`pty-stage1.log`, `pty-stage2.log`,
  `pty-stage3-auth.log`) are empirical evidence. `pty-stage2.log` is the
  reference for what Claude's TUI byte stream looks like under ConPTY.
- **Research notes** at `research/` — flue, sandcastle,
  agno-agentos, litellm, alternatives-considered, pty-pattern,
  pty-smoke-result, billing-policy-changes.
- **Documentation** at `remote-agents/orchestrator/docs/` —
  `architecture.md` and `usage.md` for the JS orchestrator.

### 9.2 What doesn't exist yet

- `remote-agents-dotnet/` directory.
- Any C# orchestrator code beyond the smoke test.
- Any UI code.
- A detailed task breakdown for the rewrite (this document is the design
  context; the *plan* is what the receiving agent should produce).
- A confirmed UI direction.

### 9.3 What's been agreed

1. Two-layer architecture: provider + agent.
2. C# rewrite is green-lit; PTY risk resolved.
3. .NET 10 with Porta.Pty as the stack.
4. Sealed `Agent` base class + abstract `ExecuteAsync` hook.
5. Discriminated `AgentEvent` hierarchy.
6. No `role` normalization; agents set provider options directly.
7. No memory primitive in v1; CLI-native `CLAUDE.md`/`AGENTS.md` cover the
   "context per run" case.
8. Migration runs in parallel with the JS orchestrator. No flag day.
9. The JS orchestrator stays the source of truth until C# reaches feature
   parity on at least the three example flows.

### 9.4 Suggested first actions for the receiving planner

1. **Read this document fully.** Then skim
   `remote-agents/orchestrator/docs/architecture.md` and
   `remote-agents/orchestrator/src/providers/claudeProvider.js` to ground
   the JS-side semantics.
2. **Decide flow file format** (recommend .NET 10 file-based programs).
3. **Decide UI strategy** (recommend defer; build UI-agnostic).
4. **Produce a detailed migration plan**: ordered task list with estimated
   effort per item, mapping each JS file to its C# counterpart.
5. **Confirm with user** before any code is written.

---

## 10. Pointers and cross-references

| Topic                              | Location                                                              |
|------------------------------------|-----------------------------------------------------------------------|
| Current JS orchestrator            | `C:\Unity\remote-unity-agents\remote-agents\orchestrator\`            |
| JS architecture doc                | `remote-agents/orchestrator/docs/architecture.md`                      |
| JS usage doc                       | `remote-agents/orchestrator/docs/usage.md`                             |
| JS claude provider                 | `remote-agents/orchestrator/src/providers/claudeProvider.js`           |
| JS codex provider                  | `remote-agents/orchestrator/src/providers/codexProvider.js`            |
| JS example full-review flow        | `remote-agents/orchestrator/flows/full-review.mjs`                     |
| Reference: Flue                    | `research/flue.md`                                       |
| Reference: Sandcastle              | `research/sandcastle.md`                                 |
| Reference: LiteLLM                 | `research/litellm.md`                                    |
| Reference: Agno AgentOS            | `research/agno-agentos.md`                               |
| Reference: alternatives considered | `research/alternatives-considered.md`                    |
| PTY pattern notes                  | `research/pty-pattern.md`                                |
| PTY smoke result (JS)              | `research/pty-smoke-result.md`                           |
| Billing policy changes context     | `research/billing-policy-changes.md`                     |
| .NET PTY smoke test artifact       | `C:\Unity\dotnet-pty-smoke\` (outside repo)                            |
| Larger infrastructure plan         | `PLANS/unity-agent-infrastructure.md`                                  |
| User memory file                   | `~/.claude/projects/.../memory/MEMORY.md`                              |

---

*End of handover. The next document a planner should produce is a concrete,
ordered implementation plan with effort estimates, derived from this design
context.*
