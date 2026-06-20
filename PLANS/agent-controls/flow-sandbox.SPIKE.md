# Spike: per-flow sandbox lifecycle + file seam (standalone)

Status: **spike plan**, not built. Cold-readable. Sibling to
[`SPIKE.md`](SPIKE.md), which proves the *security boundary* for **one** agent
invocation (the §4 attack matrix). This spike proves the boundary holds for a
**whole flow** — many agent runs looping over one shared filesystem — **without
paying a per-run spin-up cost**, and that getting files **in at the start** and
**out at the end** is a known, trustworthy mechanism.

`SPIKE.md` explicitly defers three things to integration: *"per-task
container/VM spin-up performance"*, *"Flow/Step orchestration"*, and the move
from `--network none` to **allowlist-egress** (real agents need the Anthropic
API). Those deferrals are this spike's subject.

---

## 1. The model under test

The security model traces to
[`control-plane.research.md`](control-plane.research.md); this spike takes it as
given and adds lifecycle on top. The sandbox is the unit of a **flow**, not of an
agent run.

**Threat model (load-bearing):** guard against a *wandering long-running agent
grabbing keys* — **not** against a deliberate malicious VM escape. Every "a
container is probably enough" call below rests on this line.

- **One box per flow, not per run.** Spun up at flow start, reused across every
  agent run and loop in the flow, torn down at flow end. The shared working
  directory the flow iterates over *is the box's disk* — it persists across runs
  for free, as do the agent's session files (`~/.claude/projects/*.jsonl`), so
  resuming the same session across loops also comes for free.
- **No credential ever enters the box.** The orchestrator holds the GitHub
  token; *it* clones on the trusted side and hands **files** to the box. The
  in-box checkout is **local-only** — no remote, no creds. "Agents never push"
  becomes a fact of the environment (nothing to push to, nothing to push with),
  not a rule to police.
- **Git lives at the flow seams, on the orchestrator.** Branch / commit / push /
  merge happen on the trusted side, between sessions — never mid-session, never
  by the agent.
- **Asymmetric trust.** Orchestrator → box: broad (lifecycle + exec + read
  results). Box → orchestrator: **none** — the orchestrator drives one-way (a
  box-initiated callback is out of scope for this spike). Box egress:
  **Anthropic only**.

The single claim:

> A flow can run **M agent turns over one persistent shared worktree** at
> **near-zero per-run overhead** (one spin-up amortised across the flow), deliver
> the repo **in** and the changes **out** through a mechanism that is exact and
> auditable, and the box never holds a credential or a git remote.

The `SPIKE.md` separation guarantees (env scrub, egress block, file-only seam)
are assumed proven there and are **not** re-litigated here. This spike adds the
**lifecycle, performance, and transfer** dimensions on top.

## 2. Two open questions this spike answers

Two worries decide feasibility. Each becomes a measured result.

### Q1 — Can we spin boxes many / cheap / fast?

Hypothesis: yes, because spin-up is paid **once per flow**, not per run, and
because fast isolation is commodity. To verify, not assume:

| Substrate | Expected cold spin | Isolation | Notes |
|---|---|---|---|
| Container (Docker/Podman) | sub-second–~2s | namespace | Sandcastle/Codex-web baseline |
| Container, pooled/warm | < ~500ms | namespace | pre-pulled image, reused base layer |
| MicroVM (Firecracker) | ~125–250ms | hardware VM | own kernel; e2b/Fly use it |
| gVisor | ~low-hundreds ms | userspace kernel | a sandboxed runtime, *not* a true microVM — different isolation/spin profile |
| Full VM (cloud instance) | tens of seconds | VM | only if a microVM can't do it — it can |

These figures are *expected* values (vendor-claimed for the managed rows — see
[`substrate-abstraction.md`](substrate-abstraction.md) §6); the spike's job is to
replace them with measured numbers, not to trust them.

**Measure:** cold + warm spin time for the chosen substrate, and the *amortised*
per-run overhead across an M-run flow (target: spin / M ≈ negligible). The
deliverable is a number, not a vibe.

### Q2 — Is the file in/out seam known and trustworthy?

Raw `diff`/`patch` is the weakest option (fuzzy context match, binary
foot-guns). With a **persistent per-flow box you extract once, at flow end** —
so the seam choice is between:

| Seam | In | Out | Trust | When |
|---|---|---|---|---|
| **Mounted worktree** | bind-mount the cloned dir | orchestrator reads the dir | nothing transferred — strongest | container |
| **`git bundle`** | copy bundle in, unbundle | bundle commits out, fetch on trusted side | full objects, exact, binaries ok | microVM / no shared FS |
| raw `git diff`/`apply` | — | apply patch on trusted side | fuzzy; fails on drift | avoid unless trivial |

**Measure:** round-trip a non-trivial change (text + a binary asset + a rename)
through each viable seam; confirm byte-exact landing on the trusted side, and
that `git bundle` survives a base that advanced on the trusted side meanwhile.

## 3. Scope

**In:** per-flow box lifecycle (spin / reuse-across-runs / teardown); spin-up
timing (cold, warm, amortised); the **file-in** path (trusted-side clone →
mount/copy, remote+creds stripped from the in-box checkout); the **file-out**
path (mount read vs `git bundle`); session + worktree persistence across runs;
allowlist-egress to Anthropic only (the deferral `SPIKE.md` named).

**Out / still deferred:** the separation attack matrix itself (proven in
`SPIKE.md` — reuse it as a gate, don't rebuild it); real ConPTY interactive I/O
across the boundary; identity/provability (the signed-commit / can't-self-approve
proof in `SPIKE.md`'s rung 3); the actual A.Box Flow/Step wiring. A green spike proves a **flow-shaped** workload is cheap and
the seam is exact — it does **not** wire the orchestrator.

---

## 4. Acceptance — what "green" means

| # | Claim | Required result | Measured by |
|---|---|---|---|
| F1 | One box serves a whole flow | M agent runs, one spin-up, shared worktree intact across all M | run a 5-step fake flow; assert file written in step 1 is visible in step 5 |
| F2 | Per-run overhead is negligible | amortised spin/M ≪ a run's own time | timing harness, reported in `results/` |
| F3 | Same session resumes across runs | session id reused, context carried | resume the same `<id>.jsonl` in run 2; assert continuity |
| F4 | Files delivered without creds in box | box has the worktree, **no remote, no token** | `git remote -v` empty in box; `env` has no token (reuses `SPIKE.md` A1/A4) |
| F5 | Changes land byte-exact on trusted side | text + binary + rename round-trip identical | checksum compare after mount-read / unbundle |
| F6 | `git bundle` survives a moved base | trusted base advanced meanwhile → still applies | advance trusted branch, then unbundle/fetch |
| F7 | Box reaches Anthropic, nothing else | allowlist egress: api.anthropic.com ok, github.com blocked | `curl` both; reuses `SPIKE.md` A3 shape |

F4 and F7 lean on the `SPIKE.md` matrix — cite it, don't duplicate it. The borrowed
rows, glossed so this table reads on its own: **A1** = no token in the box's `env`;
**A3** = egress blocked; **A4** = no git remote/creds in the working dir (all defined
in [`SPIKE.md`](SPIKE.md) §4). F1–F3, F5, F6 are the new surface.

**Pass:** every row observed at its required result, captured as logged output in
`results/` (demonstrated, not asserted), for the chosen substrate.

---

## 5. Rungs (each standalone; each ≈ a day)

### Rung 0 — fake flow over a mounted worktree (proves the *shape*)
- Trusted script: `git clone` a throwaway repo to a host dir, **strip the
  remote**, bind-mount it into a container.
- Run a **5-step fake "flow"**: each step is an adversarial-but-trivial script
  that appends to `hello.txt` and reads what prior steps wrote.
- Trusted side reads the dir back and commits.
- **Targets:** F1, F4, F5 (mount seam), and F7 by `--network none`. Cheapest;
  proves persistence + the mount seam end-to-end.

### Rung 1 — timing + warm pool (answers Q1)
- Same as rung 0, but instrument cold spin, warm spin (pre-pulled image / reused
  base), and amortised overhead across M runs.
- **Targets:** F2. Output a table in `results/`.

### Rung 2 — allowlist egress (lifts the `SPIKE.md` deferral)
- Replace `--network none` with an **egress allowlist**: api.anthropic.com
  reachable, everything else (esp. github.com) blocked. Proxy or firewall rule.
- **Targets:** F7 in its real form (not just "no network").

### Rung 3 — microVM + `git bundle` seam (only if VM isolation wanted)
- Swap the container for a Firecracker/gVisor microVM with **no shared FS**;
  deliver in via copy, extract via `git bundle`; measure spin vs the container.
- **Targets:** F5/F6 over the bundle seam, F2 re-measured for the microVM.
- Skip if rung 0's mount + rung 2's egress is containment enough — given the
  stated threat model (guard against a wandering long-running agent grabbing
  keys, **not** malicious VM escape), it likely is.

### Rung 4 — session resume across runs (answers F3)
- Within one box, run the agent stand-in twice reusing one session id; assert the
  second run sees the first's context. Orthogonal — can run against any rung.

---

## 6. Suggested layout (when built)

Standalone, *outside* A.Box, reusing the `SPIKE.md` attack scripts as gates:

```
spike-flow-sandbox/
  trusted/            # clone → strip remote → mount/copy in → run flow → read out → commit
  fake-flow/          # 5 steps over the shared worktree; each logs what it sees
  seams/              # mount-read vs git-bundle round-trip checks (F5/F6)
  timing/             # cold / warm / amortised spin measurement (F2)
  egress/             # allowlist proxy/firewall config (F7)
  results/            # captured output + timing tables (the evidence)
  README.md           # F1–F7 + how to run each rung
```

The flow and the trusted side share **only** the working dir (mount) or **one
bundle** (microVM). No shared env, no credential, no remote in the box.

## 7. Definition of done

- §4 table **all-green for the chosen rung set**, with captured `results/`
  (including the Q1 timing table and the Q2 round-trip checksums).
- A one-paragraph verdict: **container+mount** vs **microVM+bundle** for A.Box,
  with the measured numbers for whichever substrates were run behind the call.
- The rows lifted into A.Box as acceptance criteria for the per-flow
  sandbox/orchestrator seam — joining the `SPIKE.md` matrix (separation) so the
  integrated criteria are *separation + lifecycle + seam* together.
