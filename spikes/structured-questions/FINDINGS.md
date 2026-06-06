# Structured Questions Spike — Findings

> Companion to [`PLANS/structured-questions-spike.md`](../../PLANS/structured-questions-spike.md).
> The auto-generated, per-run numbers live in `out/<tag>/summary.md` (git-ignored);
> this file is the durable distillation for the architecture-hardening pass.
>
> Machine: Windows 11, no WSL. `claude` 2.1.158, `codex` (codex-cli) 0.134.0,
> dotnet 10.0.203. Run 2026-06-06.

## TL;DR

The approach **works on both providers**. A directive + `<<NEEDS_INPUT>>`
sentinel + small JSON envelope reliably gets a typed question out at the output
level, with no hooks and no live terminal loop, and the C# parser lifts cleanly.
Two environment issues had to be solved to get clean data (codex sandbox on
Windows; spike build isolation), and one billing nuance is flagged for hardening.

| Claim | Verdict (smoke) |
|-------|-----------------|
| C1 — emits envelope when (and only when) blocked | ✅ both providers, once codex could execute |
| C2 — parse to typed Open/Choice, degrade gracefully | ✅ parser 14/14 fixtures; live envelopes parsed clean, 0 degrades |
| C3 — resume session keeps context | ⏳ see Part B below |

## What was built

`spikes/structured-questions/` (outside `src/`, throwaway):
- `AgentQuestion.cs`, `QuestionParser.cs` — engine-liftable, dependency-free.
  Parser exposes `TryParse` (the lift API) + `Diagnose` (metrics: sentinel /
  extracted / parsed / degraded).
- `Directive.txt`, `prompts.json` — §6 directive, §7 corpus.
- `RunClaude.ps1`, `RunCodex.ps1` — Windows-native drivers (the plan's `.sh` +
  `uuidgen`/`jq` path is not viable here; see Issue 4).
- `sandbox-template/` — throwaway repo context; **every agent run executes in a
  fresh copy**, never the real repo.
- `Harness/` — C# console: loops the matrix, runs the real parser, scores vs
  `prompts.json`, writes `results.json` + §10 `summary.md`.

## Key insights & issues

### Issue 1 — codex's default sandbox cannot spawn on Windows (HIGH)
With the plan's §6 command (no sandbox flag), **every** codex run failed its
shell commands with `windows sandbox: spawn setup refresh`, then — correctly per
the directive — emitted a `<<NEEDS_INPUT>>` envelope asking the user to "fix the
workspace sandbox." This masqueraded as a **content false-positive** on the
negative prompts (`none-1` asked when it should have just acted).

- **Root cause:** codex's OS sandbox fails to start on this box, so the agent
  can't *do* anything and falls back to asking.
- **Fix in spike:** add `-s danger-full-access`. Safe here because the harness
  already isolates every run in a throwaway sandbox copy. After the fix, codex
  correctly stayed silent on `none-1`/`none-2`.
- **Architecture implication:** the production codex path needs an explicit,
  deliberate sandbox policy on Windows — the default is unusable. This interacts
  with Oracle A9 (codex args) and belongs in `CodexProvider`. A broken executor
  is *indistinguishable at the output level* from a genuine clarifying question;
  the detector cannot tell them apart, so executor health must be a separate
  signal (exit code / stderr), not inferred from the envelope.

### Issue 2 — `claude -p` DOES authenticate under subscription on this box (resolves §6 risk)
The plan flagged "if print mode refuses to auth without a key on your box." It
does **not** refuse: `claude -p ... --output-format json` ran to a clean
`is_error:false, subtype:success` result. The §6 print-mode path is viable for
content validation here.

### Issue 3 — print mode reports a non-zero `total_cost_usd` (billing nuance, flag for hardening)
The claude JSON result carried `total_cost_usd: ~0.32` for one turn. Whether
that reflects a **subscription** charge or an **API-equivalent** cost is exactly
the Oracle A1/A2 distinction the spike deliberately doesn't settle — piped `-p`
makes `isatty()` false, so production still must drive ConPTY for correct
subscription billing. **Recorded, not resolved.** The content layer (this spike)
and the billing layer (ConPTY) stay separate, as the plan intends.

### Issue 4 — the plan's `.sh` quick-path is not viable on this box (resolved)
`uuidgen` and `jq` are absent; bash is msys (Git Bash), not WSL. Session ids are
generated with `Guid.NewGuid()` and JSON parsed natively in C#. Drivers are thin
PowerShell because .NET's `Process` cannot launch the `claude.cmd`/`codex.cmd`
npm shims directly, and the directive is multiline (passed via
`--append-system-prompt` as one PowerShell argument; codex via stdin).

### Issue 5 — "outside `src/`" ≠ "outside the repo build" (resolved)
The spike still inherited the repo's root `Directory.Build.props`
(`UseArtifactsOutput` + `TreatWarningsAsErrors`), so it first built into the
central `artifacts/` folder and the harness couldn't locate its own spike root.
Fixed with a standalone `Directory.Build.props` in the spike dir (nearest-wins),
mirroring how `prototype/` escapes the same conventions. Worth remembering for
any future throwaway under this repo.

### Insight 6 — both models follow the envelope contract well
Observed shape (both providers): reasoning prose, then the sentinel on its own
line, then a single JSON object as the final content — exactly as directed. The
parser's `LastIndexOf(sentinel)` + balanced-brace `ExtractFirstJsonObject`
handled the leading prose without trouble; **zero degrades** on live envelopes so
far. claude tends to justify at length before the envelope; codex is terser.

### Insight 7 — codex session id = `thread_id` on the first event
The codex session id is emitted as `{"type":"thread.started","thread_id":"…"}`
on the first `--json` event line. `codex exec resume <id>` takes that value. The
harness's recursive `session_id`/`thread_id` scan picks it up reliably. (Claude's
id is simpler — we mint it with `--session-id` and it round-trips in
`.session_id`.)

### Issue 8 — `codex exec resume` rejects `--cd` and `-s` (plan §6 correction, HIGH)
The plan's §6 codex *resume* command (`codex exec resume <id> --cd … -o … -s …`)
**fails** on codex 0.134.0: `error: unexpected argument '--cd' found`. The resume
subcommand has a narrower arg set (`resume [SESSION_ID] [PROMPT]` + a subset of
options) — no `--cd`, no `-s/--sandbox`. The first continuity run silently
produced an **empty** turn-2 (the resume never executed), which the
`askedAgain` heuristic misread as success.

- **Fix in spike:** drop `--cd`/`-s` from resume; set cwd via `Push-Location`
  into the sandbox; disable sandboxing with
  `--dangerously-bypass-approvals-and-sandbox` (the resume-compatible form).
- **Plan correction:** §6's codex resume line must be updated. The new-turn
  command (`codex exec …`) is fine; only `resume` differs.
- **Lesson:** validate *both* turns of a CLI contract — turn-1 and resume have
  different surfaces.

## Part A — emission reliability (N=3 matrix)

Source: `out/partA/summary.md`. Total spend recorded: **~$4.27 (claude)**; codex
cost not reported (see Insight 9).

| metric | claude | codex |
|--------|--------|-------|
| parse rate (asked envelopes that parsed) | **100%** (11/11) | **100%** (6/6) |
| false-positive (negatives that asked) | **0%** (0/6) | **0%** (0/6) |
| degrades on live envelopes | 0 | 0 |
| `open` prompts → asked `open` | **6/6** | **6/6** |
| `choice` prompts → asked `choice` | **2/6** | **0/6** |

- **`open` and negative behavior are rock-solid** on both providers: every
  genuinely-unanswerable open prompt produced a parseable `open` envelope; every
  fully-specified prompt completed silently. Parser never degraded.
- **`choice` is the weak spot, and it's structural, not a parse bug.** When the
  fork is low-stakes (`choice-2`: "run CI on push or PR?"), both models **just
  pick and proceed** — exactly what the directive's *"make a reasonable
  assumption and continue"* tells them to do. `choice-1` (new csproj TFM) split:
  claude asked 2/3, codex 0/3 (it created the project, picked `net8.0`, even ran
  `dotnet build` to verify). The "hang/freeform" metric here means *"decided
  instead of asking,"* not a hang or prose-question — no run ended with a dangling
  "?" or an unparseable ask.

### Insight 9 — codex doesn't surface a cost field; claude does
claude's `--output-format json` carries `total_cost_usd` per turn; codex's
`--json` event stream exposes no equivalent the harness could read (recorded as
0.0000). Cost/quota telemetry is therefore provider-asymmetric — relevant if the
orchestrator ever wants per-turn cost signals.

### Insight 10 — `acceptEdits` ≠ "can run builds" for claude
In Part B, claude created the csproj but **could not run `dotnet build`** —
"requires approval, which isn't available in unattended mode." So claude under
`--permission-mode acceptEdits` edits files but won't run arbitrary commands
unattended; codex under `danger-full-access` did build. Another
provider-asymmetry in what "autonomous" means.

## Part B — session continuity (claim C3)

**Validated for both providers** (codex only after the Issue 8 fix). Resuming the
session id keeps full context, and a second genuine blocker is surfaced as a new
envelope — i.e. **multi-turn clarification works**:

- **claude / open-1:** turn 2 opened with *"I have the destination now
  (`s3://acme-prod-artifacts`, `us-east-1`)"*, then asked the next real blocker
  (which project to ship, no creds). Context retained.
- **codex / open-1:** turn 2 *"Deployment to `s3://acme-prod-artifacts` in
  `us-east-1` could not complete because there are no AWS credentials"* — built
  the artifact, attempted the upload, asked only for creds. Context retained.

(`choice-1` couldn't be continuity-tested: in this run both models *decided*
rather than asked, so there was no question to answer — a direct consequence of
the §A `choice` finding.)

## Recommendation for the hardening pass

The output-level detect → envelope → lenient-parse approach is **sound and ready
to harden**. Concretely:

1. **Lift `QuestionParser` + `AgentQuestion` into the engine roughly as-is**
   (drop `Diagnose` if unwanted). C2 is proven: 14/14 fixtures + 17/17 live
   envelopes parsed, zero degrades.
2. **Treat executor health as a first-class signal, separate from question
   detection** (Issues 1 & 8). A broken sandbox/CLI is indistinguishable from a
   real question at the output level — gate on exit code / stderr, don't infer.
3. **Give `CodexProvider` an explicit Windows sandbox policy** and the corrected
   resume contract (Issues 1, 8).
4. **Design for the `open`/`choice` asymmetry.** `open` detection is reliable;
   `choice` is not, because "ask vs. assume" is genuinely stakes-dependent and
   the unattended directive biases toward assuming. Options to weigh when
   hardening: a stakes/irreversibility cue in the directive, an interaction-mode
   that tightens the ask threshold, or accepting that `choice` is best-effort and
   leaning on `open` + the resolver. **Do not treat low `choice` emission as a
   defect to "fix" with parser work — it's a prompt/policy question.**
5. **Keep billing on the ConPTY path** — the print-mode `total_cost_usd` is not
   the subscription truth (Issue 3), and cost telemetry is provider-asymmetric
   (Insight 9).
