# Live Rulebook

Each Live Rule is one real-CLI guarantee — a flow or agent against the real `claude`/`codex` CLI and
subscription, gated behind `[LiveFact]` / `RUN_LIVE=1`. Add one as smoke tests convert; enforce it in `Live/Tests/`.

## Template

### <agent/flow> <given a real prompt> → <real-world effect>
- **Why:** <the live behavior no scripted provider can prove>

## Criteria

- **one_effect:** states exactly one real-world effect of the live run, not several bundled
- **needs_live:** the effect genuinely requires the real CLI/subscription — a scripted provider could not prove it
- **why_justifies:** the **Why:** names the live behavior no scripted provider can prove, not a restatement of the header
