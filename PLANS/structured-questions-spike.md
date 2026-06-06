# Structured Agent Questions — Spike Plan

> **Purpose.** Validate, with a throwaway spike, that we can make `claude`
> and `codex` reliably emit a **structured question envelope** when they need
> input, parse it into a typed `AgentQuestion`, and **resume the same session
> with an answer** — all at the *output level*, with no live terminal loop and
> no hooks. After the spike confirms (or corrects) the approach, this doc gets
> hardened into the real architecture and implemented.
>
> **How to use this doc.** It is written to be handed to a local coding agent.
> Sections 3–6 are the design to implement; section 7 is the validation
> harness to build and run; section 8 is the file layout; section 10 is the
> report-back template. Build the spike **outside the engine** (`spikes/`,
> not `src/`) — it is a probe of model+CLI behavior, not orchestrator code.
>
> **Status.** Proposed 2026-06-06. Not yet validated. Run the spike locally
> (the box has a logged-in `claude` + `codex`), record results per §10, then
> revise.

---

## 1. What we are proving (and what we are not)

Three independent claims, each with its own test in §7:

| # | Claim | Test |
|---|---|---|
| C1 | Given a directive, the agent **reliably emits** the sentinel + a parseable JSON envelope when (and only when) it is genuinely blocked. | §7 Part A |
| C2 | We can **parse** that envelope into a typed `Open` / `Choice` question, degrading gracefully to `Open` on malformed output. | §7 Part A (parser) |
| C3 | Answering = **a new CLI run resuming the session id** keeps context — for both providers. | §7 Part B |

**Out of scope for the spike** (deliberately):

- The answer-generation policy (auto / UI / human). The harness supplies a
  hand-written answer. The resolver is a later concern.
- Tool-use / permission prompts. We sidestep these via config (§6), so the
  only question that reaches us is a content question.
- Wiring into flows, `OperationDto`, SSE, UI. The spike is headless.
- Subscription **billing** correctness. The spike validates *content
  behavior*; production drives ConPTY for billing (Oracle A2). See §6.

---

## 2. Context — decisions already made (do not relitigate in the spike)

These were settled in design discussion; the spike tests their *mechanics*,
not whether to take this path.

1. **Output-level model, not a live interactive loop.** A question is a
   *terminal outcome* of an agent run, surfaced as the run's result. Answering
   is a fresh run resuming the session id. Both the prototype and both CLIs
   already work this way: Claude's prototype flow is type → wait → `/exit` →
   teardown; Codex closes stdin and runs to completion. Neither feeds an
   answer into a live process. (See `interaction-modes.md` Q10 — same
   conclusion, output-level resume for open questions.)

2. **Respawn per turn, behind the provider seam.** We do **not** keep a warm
   `claude` TUI alive between turns. Keeping the terminal warm only saves cost
   if `claude` *itself* stays alive (the shell wrapper is cheap), and that
   collapses into the live-loop we rejected — plus Codex can't be kept warm at
   all (stdin closed, Oracle A9), which would break provider symmetry.
   Clarifications are rare, so the per-turn startup cost (Oracle A4 gap +
   splash) is paid rarely. **A warm-process-pool optimization, if ever needed,
   lives inside `ClaudeProvider` behind `IProvider` (ADR 0004)** — invisible to
   flows and to the question model. Don't build it now (YAGNI).

3. **Self-defined envelope, NOT the API's structured-outputs feature.** The
   Messages API `output_config.format` / strict tool use gives a *hard* schema
   guarantee — but it is reached with an **API key**, which breaks subscription
   billing (Oracle A1/A2). The agents run keyless over ConPTY precisely to bill
   the subscription. So API-enforced schema is **off-limits for the agents**.
   We instead instruct the model to emit a small JSON envelope and parse it —
   available on every path, provider-symmetric. We do **not** mix an
   API-key'd "judge" in: it inverts `EnvScrub` from "always strip" (a safety
   invariant, A1) into "conditionally inject" (a leak risk), and we don't need
   it — `Choice` answers match programmatically, `Open` answers come from a
   human/config.

4. **No hooks (R-ARCH-3 / D4).** The spike detects the question purely from the
   agent's **output text** — no `.claude/settings.json` hooks, no
   `~/.codex/hooks.json`, no shim. This is the forcing function that proves the
   rebuild can stay hooks-free. (The prototype's hook machinery in
   `interaction-modes.md` §5 is exactly what we're trying to *not* need.)

5. **Steer with a directive + sentinel.** The agent must *stop and emit*
   rather than hang. This is the upgrade of the prototype's `<<NEEDS_INPUT>>`
   directive (`interaction-modes.md` §9): same sentinel, but followed by a
   **JSON envelope** instead of freeform prose.

---

## 3. The question model

Replaces the prototype's `TuiPrompt | OpenQuestion` split. We drop `TuiPrompt`
(permission prompts are sidestepped by config, §6) and add option-based
questions.

```csharp
// Two shapes, sized to the requirement. Add a third only on a second real use.
public abstract record AgentQuestion(string Prompt, string RawTail)
{
    // Free-form answer: a path, a name, a value the project doesn't document.
    public sealed record Open(string Prompt, string RawTail)
        : AgentQuestion(Prompt, RawTail);

    // Pick from a fixed set. AllowFreeText => the picker may also accept a
    // typed answer not in Options.
    public sealed record Choice(
        string Prompt,
        IReadOnlyList<string> Options,
        bool AllowFreeText,
        string RawTail)
        : AgentQuestion(Prompt, RawTail);
}
```

`RawTail` carries the exact text after the sentinel (the JSON, or the whole
tail on a parse failure) for diagnostics and graceful degradation.

The agent run result gains a status + optional question (mirrors the rebuild's
current `AgentResult` in `src/RemoteAgents/Actors/Agents/AgentResult.cs`, which
today is just `Text/SessionId/ExitCode/RawOutput/Transcript`):

```csharp
public enum AgentStatus { Completed, NeedsInput }

// Spike shape — final placement decided when hardening (see §9).
public sealed record AgentOutcome(
    AgentStatus Status,
    string Text,              // final assistant text (sans envelope)
    string SessionId,
    AgentQuestion? Question); // non-null iff Status == NeedsInput
```

---

## 4. The envelope

The agent, when blocked, emits the sentinel on its own line, then a single
JSON object. One object, no surrounding prose.

**Sentinel:** `<<NEEDS_INPUT>>` (kept from the prototype directive so existing
muscle memory and any leftover fixtures still line up).

**Schema (informal — we are not enforcing it via API, we are parsing it):**

```jsonc
// Open question
{
  "kind": "open",
  "prompt": "What S3 bucket should deploys target? The project documents none."
}

// Option-based question
{
  "kind": "choice",
  "prompt": "Which target framework should the new csproj use?",
  "options": ["net8.0", "net10.0"],
  "allow_free_text": false
}
```

Field rules:
- `kind`: `"open"` | `"choice"` (required).
- `prompt`: non-empty string (required).
- `options`: non-empty string array (required iff `kind == "choice"`).
- `allow_free_text`: bool (optional, default `false`; only meaningful for
  `choice`).

Keep it this small. Small schemas are what models emit reliably *without*
enforcement — which is the whole bet.

---

## 5. Enforcement = directive + lenient parse + graceful degrade

Because there is no API guarantee, reliability comes from three cheap levers
(established in design):

1. **The directive** makes "stop and emit the envelope" the only sanctioned
   way to ask. (§6 has the exact text.)
2. **The sentinel** makes the envelope trivially locatable — we slice after it,
   we don't scrape prose.
3. **Lenient parse + degrade**: malformed JSON never hard-fails; it falls back
   to `Open(prompt = rawTail)`. Worst case is a usable free-form question.

Parser (spike reference implementation):

```csharp
public static class QuestionParser
{
    private const string Sentinel = "<<NEEDS_INPUT>>";

    // Returns null when the agent did NOT ask (normal completion).
    public static AgentQuestion? TryParse(string finalText)
    {
        var idx = finalText.IndexOf(Sentinel, StringComparison.Ordinal);
        if (idx < 0) return null;

        var tail = finalText[(idx + Sentinel.Length)..].Trim();
        var json = ExtractFirstJsonObject(tail); // strips ``` fences / prose
        if (json is null)
            return new AgentQuestion.Open(Prompt: tail, RawTail: tail);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var kind = root.GetPropertyOrNull("kind")?.GetString();
            var prompt = root.GetPropertyOrNull("prompt")?.GetString();
            if (string.IsNullOrWhiteSpace(prompt))
                return new AgentQuestion.Open(Prompt: tail, RawTail: tail);

            if (kind == "choice"
                && root.TryGetProperty("options", out var opts)
                && opts.ValueKind == JsonValueKind.Array)
            {
                var options = opts.EnumerateArray()
                    .Select(o => o.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!)
                    .ToList();
                if (options.Count > 0)
                    return new AgentQuestion.Choice(
                        prompt!, options,
                        AllowFreeText: root.GetPropertyOrNull("allow_free_text")?.GetBoolean() ?? false,
                        RawTail: tail);
            }

            // kind == "open", or a choice that lost its options → treat as open.
            return new AgentQuestion.Open(prompt!, RawTail: tail);
        }
        catch (JsonException)
        {
            return new AgentQuestion.Open(Prompt: tail, RawTail: tail); // degrade
        }
    }

    // Find the first balanced {...} in the tail. Tolerates a ```json fence,
    // a leading sentence, or trailing prose. Implement with a brace counter.
    private static string? ExtractFirstJsonObject(string s) { /* spike: brace scan */ }
}
```

`GetPropertyOrNull` is a trivial helper (`TryGetProperty` → nullable). The
spike may inline it.

---

## 6. Driving the CLIs (spike)

The spike uses **print/exec mode**, not the full ConPTY TUI choreography —
it's the simplest way to script and capture output. We are validating content
behavior; the production billing path (ConPTY, Oracle A2) is a separate
concern. **No hooks are installed** (claim C-no-hooks). Permission prompts are
sidestepped by config so they never block the run.

### Claude (Oracle A8)

`--session-id` and `--resume` are mutually exclusive.

```bash
# New session — ask an ambiguous thing in unattended mode.
SID=$(uuidgen)
claude -p "$PROMPT" \
  --session-id "$SID" \
  --permission-mode acceptEdits \
  --append-system-prompt "$DIRECTIVE" \
  --output-format json \
  > turn1.json
# .result = final assistant text (parse the envelope out of this).
# .session_id = the id to resume.

# Resume with an answer — same session, context retained.
claude -p "$ANSWER" \
  --resume "$SID" \
  --output-format json \
  > turn2.json
```

> Billing caveat: piped `-p` makes `isatty()` false (Oracle A2), so this run
> may not bill the subscription. **Irrelevant for the spike.** If print mode
> refuses to auth without a key on your box, either (a) run it just to observe
> content behavior, or (b) drive the TUI like the prototype. The claim under
> test is "does it emit the envelope + keep the session," not "does it bill
> right."

### Codex (Oracle A9, A3)

`gpt-5.5` is subscription-eligible; `gpt-5.3-codex` (the intrinsic default) is
API-only and 400s under subscription.

```bash
# New session. -o captures the final assistant message; --json streams events
# (session id appears on a thread_id/session_id line — Oracle A9).
echo "$DIRECTIVE

$PROMPT" | codex exec \
  --cd "$PROJECT" \
  -o turn1-last.txt \
  --skip-git-repo-check \
  --model gpt-5.5 \
  --json - \
  > turn1-events.jsonl
# Parse the envelope out of turn1-last.txt. Scan turn1-events.jsonl for the id.

# Resume with an answer.
echo "$ANSWER" | codex exec resume "$CODEX_SID" \
  --cd "$PROJECT" \
  -o turn2-last.txt \
  --skip-git-repo-check \
  --model gpt-5.5 \
  --json - \
  > turn2-events.jsonl
```

### The directive (appended for both providers)

Upgrades `interaction-modes.md` §9: the question must now be the **JSON
envelope**, not freeform prose.

```text
You are running in UNATTENDED mode. There is no user available to answer
clarifying questions during this turn.

If information is missing or ambiguous, make a reasonable assumption, state
the assumption explicitly, and continue with the work.

If you absolutely cannot proceed without user input — e.g. you need a secret,
a destination path you cannot infer, or a choice the project documents
nowhere — then STOP and emit, as the LAST thing in your response:

  1. the literal token <<NEEDS_INPUT>> on its own line, then
  2. a single JSON object on the following lines, and nothing after it.

Use exactly this shape:

  <<NEEDS_INPUT>>
  { "kind": "open", "prompt": "<your question>" }

or, when the answer is one of a fixed set of options:

  <<NEEDS_INPUT>>
  { "kind": "choice", "prompt": "<your question>",
    "options": ["<opt1>", "<opt2>"], "allow_free_text": false }

Rules: emit at most one such object, as the final content of your response.
Do not wrap it in commentary. Do not ask questions in any other form. Do not
end your turn with "?". Either decide and continue, or emit the envelope and
stop.
```

---

## 7. Validation plan

### Part A — schema-emission reliability (claims C1, C2)

Run a **matrix** of prompts × providers, N repetitions each, and measure.

Prompt corpus (seed; grow from runs). Each prompt is paired with an expected
outcome so we can score automatically:

| id | prompt | expected | tests |
|---|---|---|---|
| `open-1` | "Deploy the built artifact to our object storage bucket." (no bucket configured) | `NeedsInput` / `open` | does it ask openly when the answer is unconstrained |
| `open-2` | "Use the team's standard commit-signing key to sign this tag." (no key documented) | `NeedsInput` / `open` | secret it can't infer |
| `choice-1` | "Create a new library csproj for the parser." (repo mixes net8.0 + net10.0) | `NeedsInput` / `choice` | does it enumerate options |
| `choice-2` | "Add a CI job — should it run on push or on PR?" (deliberately framed as a fork) | `NeedsInput` / `choice` | binary/option framing |
| `none-1` | "Add a `/health` endpoint returning 200 OK." (fully specified) | `Completed` (no question) | **negative** — must NOT spuriously ask |
| `none-2` | "Rename the variable `tmp` to `buffer` in Foo.cs." (trivial) | `Completed` | **negative** |

Metrics (per provider):
- **Parse rate** — % of `NeedsInput` runs whose envelope parsed (target ≥ 95%).
- **Kind accuracy** — % where parsed `kind` matched expected.
- **False-positive rate** — % of `none-*` runs that asked anyway (target ≈ 0).
- **False-negative / hang rate** — % of `open-*`/`choice-*` runs that asked in
  freeform prose (no sentinel) or ran to completion without asking.
- **Degrade count** — how often the lenient parser had to fall back to `Open`.

Suggested N = 15–20 per cell. Record raw outputs (don't just tally) so failures
are inspectable and the directive can be tuned.

### Part B — session continuity (claim C3)

For one `open` and one `choice` prompt, per provider:

1. Run turn 1, capture the question + session id.
2. Hand-write an answer (for `choice`, pick an option; for `open`, a plausible
   value).
3. Resume the session id with the answer.
4. **Assert continuity**: turn-2 output references the turn-1 context (e.g. it
   proceeds with the chosen framework / supplied bucket) rather than restarting
   cold or asking the same question again. This is a human eyeball check in the
   spike; capture both turns' text in the report.

### Tuning loop

If Part A misses thresholds, iterate on the directive wording (not the
parser) first — emission reliability is a prompt problem. Record which wording
changes moved the numbers; that feedback hardens §6.

---

## 8. Spike file layout

Throwaway, outside `src/`. Suggested:

```
spikes/structured-questions/
  README.md                 # how to run, copied thresholds from §7
  Directive.txt             # the §6 directive text
  prompts.json              # the §7 corpus (id, prompt, expected)
  QuestionParser.cs         # §5 reference parser (copy into engine later)
  AgentQuestion.cs          # §3 types
  run-claude.sh             # §6 Claude commands, parameterised by prompt
  run-codex.sh              # §6 Codex commands
  Harness/                  # C# console: loops the matrix, scores, writes report
    Harness.csproj
    Program.cs
  out/                      # captured raw outputs + results.json (git-ignored)
```

Two ways to drive, pick per taste:
- **Quick eyeball:** the `run-*.sh` scripts + `jq` to extract `.result` /
  read `*-last.txt`, parse by hand. Good for the first few manual checks.
- **Rigorous metrics:** the C# `Harness` shells out to the same commands,
  loops N×corpus×providers, runs `QuestionParser.TryParse`, and emits
  `out/results.json` + a markdown summary matching §10.

The C# parser and types are written so they can be **lifted into the engine
verbatim** when hardening — keep them dependency-free.

---

## 9. From spike to architecture (after it passes)

What the spike feeds into the real rebuild:

- **`AgentResult` / outcome** (L5, `src/RemoteAgents/Actors/Agents/`): add
  `AgentStatus` + `AgentQuestion?`. Decide whether it rides on the existing
  `AgentResult` record or a wrapping outcome (the spike's `AgentOutcome` is a
  placeholder — pick when hardening, honoring ADR 0003's operation contract).
- **Detection in the provider parse step** (L6, `IProvider`): each provider's
  `DriveAsync` already returns a `DriveResult` with the final text; run
  `QuestionParser.TryParse` there so `ClaudeProvider`/`CodexProvider` surface a
  uniform `AgentQuestion`. The parser is provider-agnostic — it lives once,
  above the seam.
- **The directive** becomes the unattended system-prompt addendum, applied via
  `--append-system-prompt` (Claude) / prepended to stdin (Codex), gated on an
  interaction mode (cf. `interaction-modes.md` Q2/Q3 — mode decides what we do
  with a detected question, not whether we detect).
- **Resolver seam** (the only genuinely new abstraction): `AgentQuestion →
  answer?`. Switches on the case — `Choice` auto-matches against flow config or
  renders a picker; `Open` goes to config or human. Answering re-invokes the
  agent operation on the session id. Keep it a stub (or "fail in
  non-interactive mode", like the prototype default) until the human-in-loop UI
  exists.
- **Provider seam keeps the warm-pool door open** (§2.2) — do not add it unless
  a measured latency problem appears.

Explicitly **don't** build yet (YAGNI): a third question kind, multi-question
turns, a global pending-questions queue, the API-key judge, warm terminals.

Open questions to resolve when hardening (not in the spike):
- Final home + shape of status/question on the result type.
- Whether `RawTail` is retained in the wire DTO or dropped after parse.
- Interaction-mode default and where it lives (per-call vs flow config).

---

## 10. Report-back template

Have the spike emit this so the next pass can harden from data, not vibes:

```markdown
# Structured Questions Spike — Results (<date>, <machine>)

## Versions
- claude: <claude --version>
- codex:  <codex --version>

## Part A — emission reliability (N=<n> per cell)
| provider | prompt id | runs | asked | parsed | kind ok | false-pos | freeform/hang | degraded |
|----------|-----------|------|-------|--------|---------|-----------|---------------|----------|
| claude   | open-1    |      |       |        |         |           |               |          |
| ...      |           |      |       |        |         |           |               |          |

Parse rate (claude/codex): __% / __%   target ≥95%
False-positive rate:       __% / __%   target ≈0%
Hang/freeform rate:        __% / __%

## Part B — session continuity
| provider | prompt id | turn-1 question (parsed) | answer given | turn-2 kept context? |
|----------|-----------|--------------------------|--------------|----------------------|
| claude   | choice-1  |                          |              | yes/no + note        |
| codex    | open-1    |                          |              |                      |

## Directive changes that moved the numbers
- <wording change> → <effect>

## Failures worth seeing (paste raw output)
- <provider/prompt>: <what went wrong>

## Recommendation
- [ ] Approach holds as-is
- [ ] Holds with directive change: <...>
- [ ] Problem found: <...>
```

---

## 11. References

- `design/behavioral-oracle.md` — A1/A2 (subscription/key scrub, isatty),
  A3 (codex `gpt-5.5`), A4 (cmd→claude gap), A8 (claude args), A9 (codex args).
- `design/adr/0004-provider-seam.md` — `IProvider` owns drive + parse + the
  (deferred) resume strategy.
- `design/adr/0003-actors-operations-run-contract.md` — where the outcome type
  must sit.
- `PLANS/rebuild/03-implementation-plan.md` — L5 (agent baseline), L6
  (providers/parse), L11/R-ARCH-3 + D4 (hooks deferred — this spike's
  no-hooks bet is the forcing function).
- `PLANS/interaction-modes.md` — prototype-era design this **supersedes** for
  the question model: keep its `<<NEEDS_INPUT>>` directive idea, replace
  freeform-prose questions with the JSON envelope, drop the hook machinery
  (§5) and the `TuiPrompt` case (sidestepped by config).
- `src/RemoteAgents/Actors/Agents/AgentResult.cs` — current result shape the
  hardening pass extends.
