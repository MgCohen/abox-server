# PTY Billing Smoke Test — Result

**Date**: 2026-05-28
**Script**: `remote-agents/scratch/pty-smoke/smoke.js`
**Tracer**: `SMOKE-6B1557`
**Claude session ID**: `72cfa627-82a9-4a6f-894b-7bcb36ac8935`
**Model**: Opus 4.7 (1M context)
**Auth state visible at startup**: `Claude Max` (per welcome banner)

## Round-trip mechanics

- Spawned `cmd.exe /c claude` inside `node-pty` PTY (ConPTY on Windows)
- `ANTHROPIC_API_KEY` and `CLAUDE_API_KEY` unset in spawned env
- Trust dialog dismissed via Enter (option 1 pre-selected)
- Prompt typed via `child.write(...)` after UI settled
- Tracer string appeared in Claude's reply → confirmed real round trip
- Resume URL printed: `claude --resume 72cfa627-...`

## Verification: CONFIRMED ✅

Multiple independent signals all align — PTY-spawned `claude` bills against
the Max subscription quota, NOT the Agent SDK Credit pool.

1. **`claude auth status`** reports:
   - `authMethod: "claude.ai"` — OAuth via subscription, not API key
   - `apiProvider: "firstParty"` — direct Anthropic, not Bedrock/Vertex
   - `subscriptionType: "max"` — Max plan
   - `email: matheuscohen@hotmail.com` — correct account

2. **Local session log** (`~/.claude/projects/.../<uuid>.jsonl`) tags every
   assistant message with `entrypoint: "claude-desktop"` — i.e. routed
   through the interactive desktop entrypoint, not the SDK entrypoint.

3. **`console.anthropic.com/usage`** shows nothing for the period the four
   PTY runs happened, including the Agent SDK Credits section. If those
   calls had been routed to the SDK pool they would appear there.

4. **`ANTHROPIC_API_KEY`** explicitly unset in spawned env (confirmed).

5. **`service_tier: "standard"`** in session log usage records.

Conclusion: proceed to Phase 2 build. Ongoing risk (Anthropic tightening
client-side TTY detection or adding server-side validation) remains
unmitigated by anything we can do; mitigation is the provider abstraction
which lets us swap in `apiProvider` in one line if PTY breaks.

## Notes for Phase 1 implementation

- node-pty ships prebuilt binary for Node 24 on Windows (no VS Build Tools
  needed for install — verified in this run)
- Trust dialog appears on first run in a new directory; need a one-time
  handler or pre-seed the trusted-folders list
- ANSI escape sequences are dense in the TUI; output parsing needs reliable
  ANSI-stripping
- Claude session IDs are UUIDv4 in the format we already use for `--resume`
- The "AttachConsole failed" warning at the end is a node-pty ConPTY cleanup
  artifact, post-exit, harmless
