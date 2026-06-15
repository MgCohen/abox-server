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
