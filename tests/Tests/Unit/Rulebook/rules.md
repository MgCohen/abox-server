Template: [template.md](./template.md)
Harness: [Rulebook convention](../../../Harness/README.md)

### AutoPolicy on a dangerous Bash command → denied with a guardrail reason
- **Why:** the autonomous guardrail must refuse destructive shell commands (rm -rf, force push, curl|sh, sudo,
  recursive deletes) so an unattended agent can't wreck the machine; the reason names the guardrail so the
  denial is auditable.

### AutoPolicy on an ordinary Bash command → auto-approved
- **Why:** routine commands (build, test, status, ls) must pass without a human so autonomy isn't stalled by
  safe work; the "auto-approved" reason distinguishes a pass from a guardrail block.

### AutoPolicy on a payload with no Bash command to inspect → allowed
- **Why:** the guardrail only screens shell commands, so a non-Bash tool (a file write) or an unparseable
  payload has nothing to deny and must default to allow rather than block blindly.

### AutoPolicy with a custom denylist → applies only those rules, replacing the built-in guardrails
- **Why:** a caller-supplied denylist must fully override the defaults so a project can scope its own rules; a
  built-in danger like rm -rf passes once the defaults are replaced, proving the override is total, not additive.

### DenyResolver on a permission request → answers Deny
- **Why:** the deny resolver must actively refuse permission prompts (not abstain) so a no-human run never
  grants a gated tool.

### DenyResolver on an open question → abstains with null
- **Why:** an open question has no safe default to invent, so the deny resolver returns null (abstain) rather
  than fabricating an answer.

### ResolverSelector given Auto → the auto-resolver with the configured cap
- **Why:** Auto always answers and could loop, so the selector must pair it with the loop cap from config to
  bound runaway self-resolution.

### ResolverSelector given Human → the human resolver, uncapped
- **Why:** a human-backed resolver self-terminates (a person stops answering), so it needs no loop cap; the
  selector must leave it uncapped.

### ResolverSelector given Deny → the deny resolver, uncapped
- **Why:** the deny resolver self-terminates by refusing, so it needs no cap; the selector must map Deny to it
  uncapped.

### Agent with a NEEDS_INPUT envelope in the output → NeedsInput carrying the question
- **Why:** the agent must surface a parked question to the caller as NeedsInput with the parsed prompt, the
  seam the resume loop and inbox depend on.

### Agent output with no envelope → Completed with the final text
- **Why:** an ordinary run with no question must terminate as Completed carrying the agent's final text, the
  normal success path.

### Agent with a non-zero exit → Faulted even when an envelope is present
- **Why:** a broken executor can emit a valid-looking envelope, so a non-zero exit must win and Fault rather
  than be mistaken for a question.

### Agent that resolves its question → resumes and completes using the answer
- **Why:** when the resolver supplies an answer, the agent must resume the same session and reach Completed
  using that answer, proving the in-process resume loop.

### Agent whose question gets a null answer → stays NeedsInput
- **Why:** a null (abstain) answer can't resume the run, so the agent must leave the question terminal as
  NeedsInput rather than loop or guess.

### Agent whose question loop never settles → Faulted when the cap is exhausted
- **Why:** an agent that keeps asking despite answers must be bounded by the resolve cap and Fault with an
  "exhausted" reason, preventing an infinite resolve loop.

### A factory-minted agent run through a flow → a Completed operation whose summary is the agent's text
- **Why:** the factory path is what composition uses, so a minted agent must actually drive the flow to completion and surface its text — proving the wiring isn't inert.

### An agent across successive calls → reuses the session minted on its first call
- **Why:** session reuse is what keeps a multi-turn conversation coherent; dropping it would silently restart the agent's context on every call.

### An agent's first call → a request carrying the prompt and baked-in project dir with no session
- **Why:** the agent must hand the provider the right working directory and a fresh-session signal, or the CLI runs in the wrong place or wrongly resumes a stale session.

### A completed agent outcome → a transcript with the agent's text turn and a session id
- **Why:** callers depend on the transcript and session id to render results and resume, so a Completed outcome that omits them is unusable downstream.

### AgentRunRequest with a blank prompt → ArgumentException
- **Why:** a blank prompt is a caller bug that should fail loudly at construction rather than launching a no-op agent run that wastes a billed turn.

### ClaudeHooks.Create → settings file wires a Stop hook to the stop-shim
- **Why:** the Stop hook is the only channel by which the agent signals completion, so the generated settings must point pwsh at the real shim or run-end is never observed.

### ClaudeHooks.Create without gating → no PreToolUse hook and no permission dir
- **Why:** the ungated path must stay zero-overhead — a stray PreToolUse hook or permission dir would silently impose interactive gating on flows that never asked for it.

### ClaudeHooks.Create with gating → PreToolUse perm-shim hook matching the mutating tools and a permission dir
- **Why:** only mutating tools (Bash/Write/Edit) may be intercepted; if the matcher or dir is wrong, dangerous tools run ungated or harmless ones stall waiting for approval.

### perm-shim script → reads the stdin payload and emits a permissionDecision against the permission dir
- **Why:** the shim is the contract between Claude's hook protocol and our process; if it ignores stdin or skips the permissionDecision field, approvals can't round-trip.

### ClaudeHooks.DrainRequests → returns each pending request once with its id and payload, then nothing
- **Why:** exactly-once draining prevents the same approval prompt from being surfaced twice and guarantees the id/payload needed to answer it survive the read.

### ClaudeHooks.DrainRequests with gating off → returns empty
- **Why:** callers must be able to poll uniformly regardless of gating; an ungated hook returning phantom requests would deadlock waiting for responses no shim will ever write.

### ClaudeHooks.Respond → writes the decision to the response file matching the request id
- **Why:** the shim correlates the reply by id, so the decision must land in resp-<id>.json or the gated tool blocks forever.

### ClaudeHooks.Respond with gating off → throws InvalidOperationException
- **Why:** responding when no gate exists is a caller bug; failing loudly stops a decision from being silently dropped into a directory nothing reads.

### ClaudeHooks.HasFired → reflects whether the signal file has content
- **Why:** completion is detected by polling HasFired, so an empty file must not read as fired (premature finish) and a written one must (missed finish).

### ClaudeHooks.ReadFinalMessage → returns the last_assistant_message from the signal file
- **Why:** the final assistant message is the run's payload; extracting the right JSON field is what turns a finished run into a usable answer.

### ClaudeHooks.ReadFinalMessage with missing or malformed signal → returns null
- **Why:** a half-written or garbage signal file must degrade to null rather than throw, so a racey or crashed run doesn't take the reader down with it.

### ClaudeHooks.Dispose → removes the temp artifacts it created
- **Why:** the hook scatters settings/shim/signal files into temp; without cleanup on dispose, repeated runs leak files and stale state can bleed into later runs.

### ResolveSessionFile with a staged session id → returns the matching .jsonl path regardless of folder name
- **Why:** resolution must key off the session id, not the project-folder name, or sessions staged under hashed/renamed dirs would be unfindable.

### ResolveSessionFile with an unknown session id → returns null
- **Why:** a miss must be a clean null rather than an exception or a wrong-file match, so callers can branch on absence safely.

### TryReadLastAssistantText with no session file → returns null
- **Why:** a not-yet-written transcript is normal, so absence must read as null instead of crashing the caller mid-flow.

### TryReadLastAssistantText for a turn → returns the assistant's reply text for that prompt
- **Why:** the reply text is the agent's actual answer surfaced to the user, so the happy path must extract it from the JSONL faithfully.

### TryReadLastAssistantText with multiple text blocks → joins them newline-separated in order
- **Why:** a single reply can stream as several text blocks, so they must be reassembled in order or the answer reads fragmented or scrambled.

### TryReadLastAssistantText with thinking and tool_use blocks → returns only the text blocks, excluding reasoning and tool noise
- **Why:** internal reasoning and tool invocations are not the user-facing answer, so leaking them would expose private chain-of-thought and clutter the reply.

### TryReadLastAssistantText with a prompt hint → anchors on the matching user message and returns that turn's reply
- **Why:** matching on the prompt pins extraction to the intended turn so an earlier turn's answer is never returned for the current request.

### TryReadLastAssistantText with no prompt hint → falls back to the last user turn's reply
- **Why:** without a hint the most-recent turn is the sensible default, so the method still yields the current answer rather than nothing.

### TryReadLastAssistantText with tool_result user messages → ignores them as turn boundaries and spans the real turn
- **Why:** tool results are written as synthetic user messages, so treating them as turn breaks would truncate the reply before the agent's final text.

### TryReadLastAssistantText with bare string message content → treats it as a single text block
- **Why:** the CLI may emit content as a raw string instead of a block array, and silently dropping that shape would lose the entire reply.

### TryReadLastAssistantText with an assistant turn carrying no text blocks → returns an empty string
- **Why:** a tool-only turn produced an answer but no prose, so empty string distinguishes 'spoke nothing' from null's 'no turn found'.

### TryReadLastTurnTranscript with no session file → returns null
- **Why:** the full-transcript reader must also treat a missing file as a clean absence so callers can uniformly handle not-yet-started sessions.

### TryReadLastTurnTranscript for a turn → returns every block (thinking, text, tool_use, tool_result) tagged with its kind in source order
- **Why:** the transcript view must faithfully preserve block kinds and their ordering so the rendered turn matches what actually happened.

### TryReadLastTurnTranscript with a large tool_use input → preserves the full tool input args untruncated
- **Why:** truncating tool input would hide what the agent actually did (e.g. the file content it wrote), defeating the transcript's audit purpose.

### ClaudePermission.ToQuestion on a tool call → an Allow/Deny choice naming the tool and its target
- **Why:** the human approving the call must see exactly which tool and which file/command is at stake, so a misleading or generic prompt can't trick them into approving something destructive.

### ClaudePermission.ToQuestion on an unparseable payload → a still-valid Allow/Deny question instead of a throw
- **Why:** a malformed payload from the CLI must still surface a safe, answerable gate rather than crashing the approval flow and stranding the agent.

### ClaudePermission.Describe on a tool call → the tool name and its full untruncated detail
- **Why:** the guardrail evaluating the request needs the complete command, not a display-truncated one, or a dangerous suffix could slip past unseen.

### ClaudePermission.ToQuestion on an overlong command → a truncated prompt marked with an ellipsis
- **Why:** the user-facing prompt must stay readable, and the ellipsis signals content was cut so nobody assumes they're seeing the whole command.

### ClaudePermission.RenderResponse with a decision → the hook-specific PreToolUse envelope carrying that decision and reason
- **Why:** Claude's PreToolUse hook only honors the exact envelope shape and field values, so a drifted schema would silently break permission enforcement.

### PermissionMode given a permission policy → the matching Claude permission-mode flag value
- **Why:** the CLI silently ignores an unknown mode value, so each policy must map to the exact string Claude accepts or permission gating breaks invisibly.

### BuildArgs for a fresh run → leads with --session-id and omits --resume
- **Why:** a fresh session must mint a new id; emitting --resume instead would attach to a stale or nonexistent session and lose the run.

### BuildArgs for a resumed run → leads with --resume and omits --session-id
- **Why:** resuming must reconnect to the prior session; passing --session-id instead would fork a brand-new session and drop the conversation history.

### BuildArgs with permission mode, model, and system-prompt file → each as its paired flag and value
- **Why:** these flags carry the run's policy, model choice, and injected prompt; a missing or mis-paired flag silently changes what the agent runs as.

### BuildArgs with a settings file → emits --settings paired with that path
- **Why:** the settings file is how the run-agent hooks reach the CLI, so the flag must be wired to the supplied path or the hooks never load.

### BuildArgs with no settings file → omits --settings entirely
- **Why:** passing --settings with an empty or missing path would make the CLI read the wrong settings; absence must mean no flag at all.

### BuildArgs with blank optional fields → omits --permission-mode, --model, and --append-system-prompt-file
- **Why:** blank values must not surface as empty-string flags, which the CLI would reject or misinterpret rather than fall back to its defaults.

### IsPromptReady given the input-bar footer in any permission mode → true
- **Why:** readiness is detected from the footer text, which differs between bypass and default modes, so both renderings must be recognized or the orchestrator stalls waiting to send input.

### IsPromptReady given a startup-dialog screen → false
- **Why:** sending input while a trust/confirmation dialog is up would type into the dialog, so readiness must not fire until the real input bar appears.

### DetectStartupDialog given a known dialog's text → its StartupDialog classification
- **Why:** each dialog needs a different automated response, so misclassifying trust vs bypass-warning would dismiss the wrong prompt and stall or mis-configure the run.

### DetectStartupDialog given ordinary output → null
- **Why:** normal CLI chatter must not be mistaken for a dialog, or the orchestrator would inject phantom keystrokes into a live session.

### DetectStartupDialog given dialog text split by ANSI escapes → still classifies it
- **Why:** the terminal interleaves color/style escape codes through the prompt text, so detection must see past the noise or it would miss real dialogs on a styled terminal.

### EnvScrub maps each agent to its own billing keys → claude scrubs the Anthropic keys, codex the OpenAI key
- **Why:** oracle A1 — each CLI bills the metered API instead of the subscription if its billing key is visible, so codex must guard OPENAI_API_KEY just as claude guards the Anthropic keys; the lists are per-agent so a stray key for one CLI never blocks the other.

### BuildCredentialEnv with a setup token → injects it as CLAUDE_CODE_OAUTH_TOKEN and never an API key
- **Why:** the box must bill the owner's subscription, so the credential must arrive under the OAuth-token var while no ANTHROPIC_API_KEY is set — an API key would silently switch claude to metered billing (oracle A1). It rides `docker run`, not the exec line, so the token never lands on the PTY-echoed launch line.

### BuildCredentialEnv with no setup token → carries no credential env
- **Why:** an unprovisioned box must not fabricate a credential; omitting the token leaves the turn unauthenticated rather than mis-billed, which is the deferred-validation gate (B1/B2).

### BuildBoxEnv never carries the credential → the token never reaches the PTY-echoed exec line
- **Why:** the exec line is typed into the host PTY, which echoes it into the drive buffer (logged, surfaced); keeping the credential out of the per-exec env is what makes the token leak-safe on the transcript, with `docker run` carrying it instead.

### BuildBoxEnv with an egress proxy → routes the box out through HTTPS_PROXY and HTTP_PROXY
- **Why:** the box's only sanctioned route is the allowlist proxy, so both vars must point at it or the box either can't reach Anthropic or slips the egress boundary that keeps the in-box token leak-safe.

### A credentialed box with no confining network or proxy → refused before it opens
- **Why:** the per-turn token is only safe behind the egress boundary (ADR 0013 decision 4), so a box holding it on docker's default bridge must fail closed rather than expose a silent exfil channel.

### A credentialed box behind the egress network and proxy → permitted to open
- **Why:** the sanctioned configuration — token plus confining network and proxy — must pass the guard, or every real billed turn would be blocked.

### A box with no credential → permitted to open without egress confinement
- **Why:** an unbilled turn carries nothing to exfiltrate, so the confinement requirement must apply only when a credential is present, leaving unprovisioned/dev runs workable.

### BuildArgs with no session id → a fresh exec run that reads the prompt from stdin
- **Why:** a fresh turn must not silently inherit a prior session, and the CLI only receives the prompt if stdin ("-") is wired as the final arg.

### BuildArgs with a session id → an exec resume run that threads that session id
- **Why:** conversation continuity depends on resume carrying the exact prior session id, or the agent loses all context from earlier turns.

### BuildArgs → carries the working dir, output path, OS-aware sandbox, model, and JSON flags
- **Why:** each flag is load-bearing for correct execution, and the Windows sandbox bypass prevents codex's sandbox from failing to spawn on a platform that can't host it.

### BuildArgs with a blank model → omits the --model flag entirely
- **Why:** passing an empty --model value would override the CLI's configured default with garbage instead of falling back to it.

### ScanSessionId over any known JSON shape → the embedded session id
- **Why:** codex emits the id under several flat and nested key names across versions, so the scan must tolerate every shape or session resume breaks.

### ScanSessionId when no valid id is present → null
- **Why:** non-JSON tracing, too-short ids, and unrelated keys must not be mistaken for a session id and threaded into a bogus resume.

### ExtractTranscript over a completed agent_message → a single Text turn carrying its text
- **Why:** the user-visible assistant reply must surface as exactly one Text turn with its content intact, not be dropped or duplicated.

### ExtractTranscript over a command_execution → a ToolUse turn followed by a ToolResult turn with exit code and output
- **Why:** a tool invocation and its outcome are distinct facts; splitting into ToolUse then ToolResult preserves the exit code and output the reader needs to judge what happened.

### ExtractTranscript over a completed reasoning item → a single Thinking turn carrying its text
- **Why:** model reasoning must be classified as Thinking, not regular Text, so the UI can render or hide it distinctly from the actual reply.

### ExtractTranscript over mixed tracing and incomplete events → only the completed items become turns
- **Why:** non-JSON tracing lines and item.started drafts are noise; treating them as turns would leak internal chatter and duplicate not-yet-final content.

### ExtractTranscript over empty output → no turns
- **Why:** an empty run must produce an empty transcript rather than a spurious turn or a crash on the degenerate input.

### Agent auto-resolves a question → the decision is recorded on the run ledger as an Auto-sourced answered Question
- **Why:** auto-resolution must leave an auditable trail (kind, prompt, source, answer) so an unattended run's automated choices can be reviewed and trusted after the fact rather than vanishing silently.

### Flow that runs every operation successfully → Completed with each operation recorded as Completed
- **Why:** the orchestrator's whole job is to drive a flow to a terminal success; if the phase or per-operation status drifted, the UI would show a half-finished or stuck run for work that actually succeeded.

### Snapshot version after a run → strictly greater than before the run
- **Why:** clients diff on the version to detect new state; a non-monotonic version would let a stale snapshot masquerade as current and silently drop updates.

### Flow whose operation throws → Failed with the operation Failed and its error recorded
- **Why:** failures must surface as a Failed phase with the actual error text, otherwise a broken run looks healthy and the operator never sees why it died.

### Flow that passes context data into an operation's args → that data appears in the operation's recorded result
- **Why:** flows are useless if request/context data can't reach the operation that consumes it; this proves the data plumbing from context through args is wired.

### Flow that runs operations concurrently → every operation recorded exactly once with no loss or corruption
- **Why:** the snapshot recorder is shared mutable state, so concurrent operations could race and lose or duplicate entries; this guards the thread-safety of the run record.

### Changes subscribed after a run finished → yields the latest snapshot once then completes
- **Why:** a late subscriber must still get the final state and a clean stream completion, otherwise it would hang waiting or miss the result entirely.

### FlowDefinition built from a type that is not a Flow → throws ArgumentException
- **Why:** catching a non-Flow type at definition time turns a wiring mistake into a loud construction error instead of a runtime cast failure deep in execution.

### FlowDefinition built from a concrete Flow type → exposes that type as its FlowType
- **Why:** composition relies on FlowType to instantiate the right flow; if it didn't round-trip the supplied type, the registry would build the wrong flow.

### Force push to a protected branch → refused with InvalidOperationException
- **Why:** a force push to main/master can silently destroy shared history, so the guardrail must hard-fail before the dangerous command ever reaches git.

### Commit with an invalid input (no files or blank message) → refused with ArgumentException
- **Why:** an empty file set or whitespace-only message yields a meaningless or empty commit, so the call must be rejected at the boundary rather than producing junk history.

### Status on a dirty tree → Paths lists each modified and untracked path
- **Why:** callers decide what to stage/commit from this list, so both already-tracked edits and brand-new files must surface or work silently gets dropped.

### Status → IsDirty reports whether the working tree has uncommitted changes
- **Why:** the dirty flag gates whether a commit step runs at all, so a false negative would skip persisting real work and a false positive would commit nothing.

### Commit of listed files → stages and commits them, returning the full hash and subject and leaving the tree clean
- **Why:** downstream steps key off the returned 40-char hash and subject to reference the commit, and a residual dirty tree would prove the stage didn't actually capture the file.

### Diff on a dirty tree → reports the changed-file count and the diff text naming each file
- **Why:** the diff is what gets shown to the model/user for review, so the count and per-file text must reflect the real edits, not a stale or empty snapshot.

### Status after a reverting checkout → reports a clean tree
- **Why:** a revert that still showed phantom changes would trigger needless commits and mislead the dirty check, so state must reset to truly clean.

### Force push to a remote that advanced since the last fetch → refused before it can overwrite
- **Why:** the cascade force-pushes rebased branches; `--force-with-lease --force-if-includes` must reject a stale overwrite so a collaborator's pushed work is never silently lost (a lease alone is defeated by a background fetch — spike research/stacked-prs.md §9).

### Agent emitting NEEDS_INPUT → blocks on a pending decision until a human resolves it, then resumes to Completed
- **Why:** the whole point of interactive resolution is that the run must pause for human input rather than guessing or failing, and must actually carry the human's answer forward to finish the work.

### Run cancelled while awaiting a decision → unblocks as terminal NeedsInput and drops the pending decision
- **Why:** a cancelled run must not leave the await hung forever nor leave an orphan decision sitting in the queue, so cancellation has to settle to a terminal outcome and clean up state.

### ComposeSystemPrompt in Auto → emits the unattended directive
- **Why:** unattended runs must get a directive that lets the agent proceed without a human, so Auto must not accidentally produce the ask-first text that would stall the flow.

### ComposeSystemPrompt in Human → emits a distinct interactive ask-first directive
- **Why:** human-supervised runs depend on the agent pausing to ask; if Human collapsed to the unattended directive the agent would guess instead of waiting, defeating the whole interactive mode.

### ComposeSystemPrompt in either mode → appends the directive after the role prompt and includes the envelope sentinel
- **Why:** the role prompt must survive composition and both modes must carry the parser's sentinel, otherwise downstream question parsing can't recognize the agent's structured output regardless of resolution.

### AutoResolver on an open question → self-answers a non-empty proceed instruction tagged with the Auto source
- **Why:** an empty or untagged answer would stall the agent or misattribute the decision; the resolver must keep unattended flows moving and record that the answer was machine-generated.

### AutoResolver on a permission choice → never self-answers Allow
- **Why:** auto-allowing a permission would let an unattended run authorize destructive actions like rm -rf, so degrading to deny is the safety boundary that must hold.

### Codex resume → reuses the prior session via bypass, without re-setting cd or sandbox
- **Why:** a resume must continue the existing CLI session as-is; re-asserting --cd/--sandbox would fork context or re-prompt approvals, so resume relies on the already-granted bypass instead.

### Codex new turn → sets cd and the OS-specific sandbox default
- **Why:** a fresh turn has no inherited session, so it must explicitly anchor the working directory and pick the per-OS sandbox so the agent runs with correct scope on Windows vs non-Windows.

### Codex driven with a non-bypass policy → throws an actionable NotSupportedException naming the policy
- **Why:** Codex only operates under full bypass; silently downgrading or running an Ask/other policy would mislead the caller, so it must fail loudly with the offending policy in the message.

### Directive composed with a role prompt → preserves the role text and appends the directive
- **Why:** the agent needs both its role instructions and the safety directive (question sentinel, irreversible-action guidance); dropping either would break role behavior or unattended safety guarantees.

### Directive composed with an empty role prompt → returns the standalone unattended directive
- **Why:** with no role text there must be no stray separators or empty prefix, so the unattended directive is emitted verbatim and still governs behavior.

### Text without the sentinel → null (not a question)
- **Why:** the orchestrator must only treat output as a question when the agent explicitly raised the sentinel, so ordinary completion or empty output cannot be misread as a pending prompt that stalls the flow.

### A sentinel with a well-formed open envelope → an Open question carrying its prompt
- **Why:** the prompt text is what gets surfaced to the user to answer, so a clean envelope must round-trip its prompt verbatim or the user sees the wrong question.

### A sentinel with a well-formed choice envelope → a Choice question carrying its options and free-text flag
- **Why:** the UI renders selectable options and decides whether to allow custom answers from these fields, so both the option list and the free-text flag must survive parsing intact.

### A sentinel whose JSON is wrapped in noise (fences, surrounding prose) → the embedded object is still parsed
- **Why:** LLMs habitually wrap JSON in markdown fences or chat-style prose, so the parser must locate the real object inside that noise rather than failing on realistic agent output.

### Braces inside a JSON string value → the object scan stays balanced and parses correctly
- **Why:** a naive brace-counting scan would terminate early on a brace inside a string literal, truncating valid prompts; honoring string context keeps legitimate content intact.

### A sentinel whose JSON cannot be parsed → an Open question whose prompt is the raw tail
- **Why:** a half-emitted or malformed envelope must still reach the user as an answerable question instead of being dropped, so the raw tail becomes the prompt rather than a parse failure.

### A sentinel with no JSON object at all → an Open question whose prompt is the trailing prose
- **Why:** agents often just ask in plain English after the sentinel, so the parser must degrade to treating that prose as the question rather than requiring structured output.

### An open envelope with an empty prompt → still an Open question
- **Why:** an empty prompt is degenerate but must not change the classification away from Open, so downstream handling stays uniform regardless of prompt emptiness.

### A choice envelope missing its options → falls back to an Open question
- **Why:** a choice with no options is unanswerable as a menu, so it must degrade to a free-form Open question instead of producing an empty, unusable choice prompt.

### Multiple sentinels in the text → the last sentinel's envelope is the parsed question
- **Why:** streamed output may mention the sentinel earlier or contain a stale one, so only the final occurrence reflects the agent's current ask and must win.

### RunCommand running a successful command → result with exit code 0, captured stdout, and TimedOut false
- **Why:** callers branch on ExitCode and read Stdout to act on output, so a successful run must report success and faithfully surface what the process printed, not lose or corrupt it.

### RunCommand running a command that exits non-zero → result carrying that exact exit code, TimedOut false
- **Why:** the exact exit code is how callers distinguish failure modes; collapsing it to a generic non-zero or mislabeling failure as a timeout would hide why a step actually failed.

### RunCommand whose command outlives the configured timeout → result with TimedOut true
- **Why:** a hung child process must be detectable and reapable rather than blocking forever, so the timeout has to be observable distinctly from an ordinary exit.

### EnsureOk on a non-zero result → throws InvalidOperationException naming the failed step
- **Why:** EnsureOk is the fail-fast guard pipelines rely on, so a failed step must abort loudly with a message that pinpoints which step broke.

### ClaudePermission.IsAllow on an answer → true only for a case-insensitive trimmed "allow", else false
- **Why:** approval must fail-closed: blanks, nulls, denials, or anything ambiguous must never be read as consent to run a tool.

### Project.Create → a project with a trimmed, non-blank name
- **Why:** the create door is the single home of the name invariant — a project cannot exist nameless;
  surrounding whitespace is trimmed and a blank or whitespace-only name is rejected.

### Project.Create → a project whose required path is stored absolute
- **Why:** a project must point at a directory to be launchable; the create door rejects a blank path and
  normalizes whatever it accepts to an absolute path (existence is checked later, at resolve-time).

### Project.Rename → a renamed project with a trimmed, non-blank name
- **Why:** rename is a mutation door and enforces the same name invariant as Create, leaving the project's
  identity (`Id`) and path unchanged.

### Project.MoveTo → a relocated project with an absolutized path
- **Why:** the only door that changes a project's path enforces the same path invariant as Create, leaving
  the project's identity (`Id`) and name unchanged.

### ProjectRepository.GetByName → the project matched case-insensitively, null when absent
- **Why:** name uniqueness on create and project resolution on flow-launch share one query home — a
  case-insensitive name lookup over the store — so the rule isn't duplicated across the two callers.

### ProjectResolver.Resolve → the project for a known id, else a clear failure
- **Why:** flow-launch is keyed by project id. A known id resolves to its stored Project (name for the run
  label, path for the working dir); an unknown id throws, and a stored path whose directory is gone throws —
  so a launch never starts against a non-existent directory.

### JsonRepository → round-trips entities through Add, Get, Update, and Remove
- **Why:** the storage seam's core contract — an entity written through the repository is read back, replaced,
  and deleted by id, with `GetAll`/`GetById` reflecting each mutation.

### JsonRepository on a fresh instance → reloads persisted entities
- **Why:** writes are durable — a new repository over the same store sees everything a prior instance wrote,
  proving persistence is on disk, not just in the in-memory cache.

### JsonRepository with an unreadable backing file → starts empty
- **Why:** a corrupt or unreadable store is non-fatal — the repository starts empty and a subsequent write
  recovers it, so a bad file never crashes startup.

### JsonRepository under concurrent writers → no torn store
- **Why:** the `SemaphoreSlim` + atomic temp→`File.Replace` write means concurrent `Add`s all land and the
  on-disk file always parses — no torn write under contention.

### Inbox.Get → the item by id, or null when absent
- **Why:** a read is pure — fetching an item must not change it (seen is an explicit, client-driven signal, not
  a side effect of a read), so GET stays safe to cache/retry; a missing id returns null rather than throwing.

### Inbox.MarkSeen → the item stamped seen once and stable on repeat, null when absent
- **Why:** "seen" is reported by the client, the only authority on what a human actually viewed; the surface
  stamps SeenAt on the first mark and a re-mark must not move it, with a missing id returning null.

### Inbox.Complete → the item marked complete once and stable on repeat, null when absent
- **Why:** completion is the terminal interaction stamp the surface drives; the first complete records when the
  item was resolved and a later call must not move it, with a missing id returning null.

### Inbox.Query → items carrying every requested tag (matched case-insensitively) in arrival order, all when no tag given
- **Why:** the inbox is a flat chronological feed with an AND tag filter — no tag returns everything in arrival
  order, a tag set narrows to items carrying all of them, and tags are human labels so the match ignores case —
  so the surface can scope without a priority engine.

### InboxItem persisted through the repository → reloads as its concrete subtype
- **Why:** the inbox holds a polymorphic item hierarchy in the shared JsonRepository, so an item written and read
  back from a fresh repository must round-trip as its concrete subtype (not the abstract base or a wrong type),
  proving the type discriminator survives persistence.

### Decisions.Raise → stores the question and pushes a matching inbox item sharing its id
- **Why:** a raised decision must both persist and surface in the inbox under the same id, so the human sees it
  in the feed and the answer can close both sides through one identifier — the dependency points Decision → Inbox.

### Decisions.Get → the decision by id, or null when absent
- **Why:** a read is pure — fetching a decision must not change it — so it stays safe to retry; a missing id
  returns null rather than throwing.

### Decisions.List → every decision in arrival order
- **Why:** the decision feed is flat and chronological like the inbox, so listing returns all decisions ordered by
  creation (id as a stable tiebreaker), giving the client a deterministic order without a priority engine.

### Decisions.Answer → the decision recorded with its yes/no answer once and stable on repeat, null when absent
- **Why:** answering records the human's yes/no (with an optional note) the first time and a re-answer must not
  move the recorded answer or its timestamp; a missing id returns null rather than minting one.

### Decisions.Answer → completes the inbox item it raised
- **Why:** resolving a decision must complete the inbox item raised under the same id, so the feed reflects the
  decision is handled without the client reconciling two surfaces by hand.
