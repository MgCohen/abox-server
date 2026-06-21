---
status: accepted
date: 2026-06-21
supersedes:
amends: R-SPINE-1
---

# ADR 0013 — Agent turns execute in an ephemeral per-turn sandbox

## Amendment (2026-06-21) — transport: host PTY wraps `docker exec`, no in-box driver

Decision 3 originally moved the PTY choreography into an **in-box driver**. Implementing
it against the real `ClaudeProvider` showed that was the wrong cut: `ClaudeProvider`
already owns a complete, tested PTY drive (dialog dismissal, prompt-ready detection,
submit, `ClaudeProtocol`). Re-implementing it as an in-box script in another language
duplicates the protocol logic and invites drift. Instead the host keeps the PTY
choreography and launches the in-box `claude` through **`docker exec -it`**: the `-t`
allocates a TTY inside the box (so `isatty`/billing holds) and the TUI is forwarded to
the host's PTY. The box runs only `claude` + its sh hooks. This **revises decision 3**
(and its `Confirmation` item) as restated above; everything else in the ADR stands. The
"brain on host / hooks cross the mount" intent is unchanged — strengthened, even, since
*more* logic stays host-side.

## Context

Today the orchestrator and the agent CLI run on the **same machine**:
`ClaudeProvider.DriveAsync` spawns `claude` as a host PTY subprocess in the project
directory, sharing the host's filesystem, environment, and ambient auth. That was
fine for a single-tenant prototype, but the rebuild's target is a multi-project
service driving an **untrusted** model agent that can run arbitrary tools against a
**writable repo** while holding the owner's **subscription credential**. Three forces
now need a boundary that the host-subprocess model does not provide:

1. **Blast radius.** A misbehaving or prompt-injected agent on the host can read
   anything the orchestrator can — other projects, the host's secrets, the network.
2. **Credential exposure.** The agent's *model* credential must live where the agent
   runs (unlike git/repo creds, which stay host-side — see `SPIKE.md` A1/A4). On the
   host, that token sits in the orchestrator's own environment.
3. **Exfiltration.** With arbitrary tool use and full network, a leaked token or repo
   contents can leave over any socket.

A spike (`spikes/agent-in-box/`) validated, on a real Docker daemon, that a Linux
container can carry a `claude` turn with the hook/JSONL choreography intact, that the
only egress can be pinned to Anthropic, and that the credential can be injected
ephemerally. This ADR records the decision that spike argues for; it does **not**
restate the mechanics (those live in the spike docs and the code).

## Decision

We will **run each agent turn inside an ephemeral, isolated sandbox ("the box"), not
as a host subprocess.** Concretely:

1. **The box is the unit of isolation, scoped to one turn.** One sandbox is opened per
   `DriveAsync` call and torn down when the turn ends — **not** per agent, per session,
   or per host. An "agent" is a durable role spanning many turns and projects; it is
   the wrong unit. Per-session warm boxes are an explicit *non-decision* here (see
   revisit trigger).
2. **Turn state lives on a bind mount, so a box is disposable.** The transcript (JSONL),
   hook signal, and permission files resolve to a host-mounted directory. Resume reads
   that mount, so a box dying after each turn loses nothing — this is what makes
   per-turn lifetime cheap and correct.
3. **The brain *and* the PTY choreography stay on the host; only `claude` + its sh
   hooks run in the box.** The resolver, `AutoPolicy`, JSONL parsing, the pump/resolve
   loop, *and* the PTY drive (dialog dismissal, prompt-ready, submit) stay in the
   host-side provider. The host drives the in-box `claude` over `docker exec -it` — the
   `-t` allocates a TTY *inside* the box, so `isatty`/subscription billing holds, while
   the rendered TUI is forwarded to the host PTY. The existing **file-polling hook IPC
   crosses the bind mount unchanged** — this is the load-bearing reason the seam is cheap
   (ADR 0006/0007 mechanisms are boundary-transparent). See the same-day amendment below
   for why this replaced an in-box driver.
4. **The box holds the model credential, delivered ephemerally and brokered; egress is
   the security boundary.** The owner's subscription token is injected per turn (not
   baked into an image, not a box-wide env var) and the box's only network egress is a
   host-controlled allowlist to Anthropic. A leaked token has no exfil path. Serving
   more than the single subscription owner from one token is out of scope (ToS).
5. **`ISandbox` (open/exec/close) is the seam.** `DockerSandbox` is the first
   implementation; `DisposeAsync` issues `docker rm -f` as the guaranteed anti-zombie
   teardown (oracle A10).

## Consequences

- **Good:** a real blast-radius boundary; the credential and repo are reachable only
  from Anthropic; teardown is guaranteed by killing the box, stronger than a PTY-tree
  kill; turns are isolated, so many run concurrently without interference.
- **Good:** the rebuild's "internals, not behavior" holds — decision logic and wire
  contracts are untouched; the turn merely relocates.
- **Bad / cost:** per-turn **cold start** (container start + claude onboarding) is added
  latency; the in-box driver is new surface to maintain; the host now runs/manages a
  container runtime + egress proxy.
- **Revisit trigger:** if per-turn cold start dominates turn latency, widen the box
  lifetime to **per session** (hold open across resume turns — proven by the spike's
  hold-open result). This is a pure lifetime change, not a correctness one, because
  state already lives on the mount. Revisit also if a second sandbox backend (microVM)
  is needed, or if egress must move from allowlist to a denylist default.

## Confirmation

- [det] `ISandbox.DisposeAsync`'s implementation issues `docker rm -f` (or equivalent
  forced teardown) — guaranteed anti-zombie close (oracle A10).
- [det] Exactly one sandbox is opened per `DriveAsync` invocation (per-turn), not cached
  on the provider/agent across calls.
- [det] The box attaches to a network with no default route out; its only egress is the
  allowlist proxy (no `--network` default bridge in the run path).
- [llm] The resolver, `AutoPolicy`, JSONL parsing, *and* the PTY choreography run on the
  host side of the seam; the box runs only `claude` + its sh hooks (no in-box driver).
- [llm] The model credential reaches the box ephemerally (tmpfs/fd-injection) and is
  never baked into an image or set as a box-wide environment variable.

## Alternatives considered

- **No isolation (the prototype / status quo).** Agent runs on the host. Rejected: no
  blast radius, credential in the orchestrator's env, unrestricted exfil — the three
  forces above go unanswered.
- **Per-agent or per-session container.** A box bound to the agent role or the session.
  Rejected as the *default*: an agent spans many projects/turns and can't share one box
  under concurrency; per-session is a valid *optimization* but not needed first (YAGNI),
  and is reachable later precisely because state lives on the mount.
- **VM / microVM per turn instead of a container.** Stronger isolation, far heavier.
  Rejected for now: the egress boundary + container is sufficient for a single-owner
  service; revisit if multi-tenant.
- **Mount a long-lived credential file into the box.** Simpler than brokered injection.
  Rejected: leaves a reusable key on a writable mount; the spike showed ephemeral
  injection is achievable, so we pay for the leak-safe path.

## More Information

- Living "how": [`spikes/agent-in-box/SPIKE.md`](../../spikes/agent-in-box/SPIKE.md)
  (claim + acceptance), [`FINDINGS.md`](../../spikes/agent-in-box/FINDINGS.md) (what the
  spike proved + the auth-source correction), [`sandbox.md`](../../spikes/agent-in-box/sandbox.md)
  (the collapsed `ISandbox` seam).
- Code: `src/Infrastructure/Sandbox/` (`ISandbox`, `DockerSandbox`); the consuming
  refactor of `src/Domain/Agents/Claude/ClaudeProvider.cs` is the next increment.
- Refines [ADR 0004](0004-provider-seam.md) (the provider now drives through a sandbox)
  and rests on [ADR 0006](0006-scoped-hooks-claude-stop.md) / [ADR 0007](0007-permission-policy-pretooluse.md)
  (the hook IPC that crosses the mount unchanged).
