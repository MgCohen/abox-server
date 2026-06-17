# Spike: agent / git privilege separation (rungs 1–2, Linux)

A runnable proof of the boundary designed in
[`../../PLANS/agent-controls/control-plane.research.md`](../../PLANS/agent-controls/control-plane.research.md)
and planned in [`../../PLANS/agent-controls/SPIKE.md`](../../PLANS/agent-controls/SPIKE.md).
No A.Box Flows, Steps, ConPTY, or billing — just the seam, in isolation.

## The claim under test

> An untrusted "agent" can edit files in a working directory, but **provably
> cannot** reach the git credential, the secret-at-rest, or the network — while a
> separate **control-plane** process commits the agent's output as the bot identity.

The "agent" here is **not** an LLM — it is a deliberately adversarial script
(`fake-agent.sh`) that *attempts* every attack on purpose. We want it hostile, not
hope an LLM behaves. Each attack prints one `Ax|desc|RESULT|detail` line; the
control plane evaluates them against the per-rung required result.

## Run it

```bash
sudo bash rung1.sh   # two OS principals on one host (proves the principle)
sudo bash rung2.sh   # + net/pid/mount namespaces (closes the egress hole)
```

Each run sets up a throwaway `/opt/abox-spike` (separate `abox-worker` user, a
0600 secret, a bare "remote" + real clone), spawns the adversary, then tears it
all down. Captured evidence lives in [`results/`](results/).

## What's where

| File | Role |
|---|---|
| `control-plane.sh` | the trusted side: owns secret + clone + identity; seeds files in, reads them back, commits as the bot. Sourced by the rung scripts. |
| `fake-agent.sh` | the untrusted adversary: one line of honest work, then runs every attack. |
| `attacks/aN_*.sh` | one attack per file; each prints its result line. |
| `rung1.sh` / `rung2.sh` | wire the spawn — the **only** thing that differs between rungs. |
| `results/` | captured matrix + raw result lines = the evidence. |

## The boundary, drawn

```
CONTROL PLANE (root here / .NET host in prod)      WORKER SANDBOX (abox-worker)
  • holds secret.token (0600) + clone w/ remote       • scrubbed allowlist env
  • holds the long-lived process w/ secret in env      • only the working dir
  • git commit/push AS the bot                         • NO secret, NO .git, NO creds
        │                                                      ▲
        └──────────── shared surface: the working dir ─────────┘
                          (plain files in / files out)
```

The one thing crossing the seam is files. Git, the credential, and (rung 2) the
network never do.

## Positive controls (so the negatives mean something)

A passing attack like "the agent found no secret" is worthless if there was no
secret to find, or the search was scoped wrong. Each run first proves the targets
are **real**:

- **PC1** — the secret exists and the *control plane* can read it (prints a
  sha256 fingerprint + length).
- **PC2** — that same secret is live in the control-plane process's env (the
  thing A6 then fails to read).
- **A2** reports `EACCES` vs `ENOENT` per target — it shows the worker is
  *blocked* reading a file PC1 proved exists, not that the file is merely absent.
- **A5** runs a detector self-test first: it plants a canary it *can* reach and
  confirms the hunt finds it, *then* hunts the protected locations and finds
  nothing. "Found nothing" only counts because the detector is shown to detect.

## Results (reproduced on this Linux container)

Both rungs are **GREEN** — every row met its required result.

| # | Attack the adversary attempts | Rung 1 | Rung 2 | Enforced by |
|---|---|---|---|---|
| A1 | read the process env for a token | PASS | PASS | env scrub / allowlist |
| A2 | read the secret at rest (cp file / clone config) | PASS | PASS | separate principal + dir perms |
| A3 | reach `github.com` | *reached* | **PASS** | (rung 2) network namespace |
| A4 | `git push` from the working dir | PASS | PASS | no `.git` / no remote in sandbox |
| A5 | exfil: find a secret, then send it | *partial* | **PASS** | A1+A2+A3 together |
| A6 | introspect / signal the control plane | PASS | PASS | different principal (rung 1) → pid ns (rung 2) |
| A7 | control plane commits the agent's diff as the bot | PASS | PASS | control plane holds the identity |
| **R1** | plant `.git/hooks/pre-commit` for the harvester to run | PASS | PASS | harvest ingests file content only; hooks off on the clone |
| **R2** | plant a symlink for the harvester to commit | PASS | PASS | harvest skips symlinks (`find -type f`) |
| **R3** | TOCTOU: flip a file to a symlink-at-secret between `find` and `cp` | PASS | PASS | copy with `--no-dereference` + strip symlinks; anti-zombie teardown |

Rung 1 deliberately leaves **A3 reachable** and **A5 partial** — it has no egress
control. That is the exact gap rung 2 closes with a network namespace: A3 flips to
blocked and A5 to "nothing found, nowhere to send". A6 also strengthens: rung 1
denies the control-plane's `/proc/<pid>/environ` by ownership; rung 2 makes the
process **invisible** via the pid namespace.

### The return path (R1/R2) — the hole an adversarial review found

A1–A6 cover the agent attacking *outward* from its sandbox. The dangerous seam is
the **return path**: `cp_harvest` ingesting the agent's working tree back into the
trusted clone. A red-team review found that a naive `cp -rf "$WORK/." "$CP_CLONE/"`
copies a worker-planted `.git/hooks/pre-commit` into the live clone, and the next
`git commit` **runs it as the control plane (root)** — defeating every other row at
once, without the agent ever escaping its sandbox. "The seam is data, not control"
was *false as implemented*.

The fix: harvest copies **regular file content only** — never `.git`, hooks, or
symlinks — and commits with `core.hooksPath=/dev/null`. R1/R2 are the exploit
turned into permanent regression tests, and they are validated by a negative
control: with the naive harvest restored, **both R1 and R2 fail** (hook executes,
symlink committed); with the fix, both pass.

#### Round 2 (R3): the TOCTOU race the first fix missed

A second red-team pass broke that first fix. `cp` (without `--no-dereference`)
**follows symlinks and runs as root**, *after* `find` recorded the file types — so
a worker process that **survives the agent** (rung 1 has no pid namespace) flips a
regular file to a symlink at `$CP_SECRET` in the gap, and root copies the **secret
content** into the pushed commit. It leaked on the first try at natural speed while
R1/R2 still passed (the committed file is regular, not a symlink).

Fixes: copy with `--no-dereference` (the link, never its target) + strip any
symlink before commit + **anti-zombie teardown** (`pkill -KILL -u` the worker, so
no process survives to drive the race; rung 2's pid namespace already does this
structurally). R3 reproduces the exact find→cp window **deterministically** —
because a live race is flaky and a flaky security test gives false confidence — and
drives the *real* copy primitive. Negative control: revert `--no-dereference` and
R3 fails 5/5; with the fix it passes.

The lesson: the outward perimeter (A1–A6) was sound from the start; **every real
hole was on the return path** — the control plane ingesting an attacker-owned tree.

## Rung → mechanism → A.Box / Windows mapping

| Rung | This container | A.Box target | Windows equivalent |
|---|---|---|---|
| 1 | separate OS user + scrubbed allowlist env + dir perms | separate OS principal for agents | separate Windows user; per-user DPAPI / Credential Manager |
| 2 | `unshare --net --pid --mount` (no daemon) | container per task | Windows container / `--network none` |

The mechanisms differ per OS; the **attack matrix is the portable artifact**. It
lifts back into A.Box unchanged as the acceptance criteria for the real
control-plane/sandbox seam.

## Honest scope — what this does NOT prove

- **Linux only.** Proven on Linux primitives (separate user, namespaces). Re-run on
  Windows to validate the DPAPI / Windows-container equivalents — deferred by choice.
- **Mount isolation is partial.** Rung 2 isolates net + pid; the agent's filesystem
  view is still the host's (secret-at-rest is held out by *perms*, as in rung 1). A
  real container/Docker bind-mounts only the working dir — the stronger form, noted
  but not built here (no Docker in this environment).
- **Egress is proven for `--net none`, not the allowlist case A.Box will ship.**
  The research doc (§3.4) says the real sandbox is *allowlist-egress* (LLM API
  permitted) — which must still deny the loopback git proxy + DNS while allowing
  one endpoint. Rung 2 proves the easy case (no network at all); the allowlist
  variant is integration work.
- **Rung 1 has no egress control — and that is more than the A3 row shows.** A
  red-team pass confirmed a rung-1 worker can reach an ambient credential-injecting
  git proxy (authenticated access to the real remote, no token needed) and
  exfiltrate to any host. Rung 1 is "principle only"; egress is exactly what rung 2
  adds, and it is the single highest-value control.
- **Rung 3 (identity & provability) is not here.** "Unsigned commit → rejected" and
  "bot can't self-approve its own PR" are server-side GitHub App + ruleset
  guarantees — to be discussed and stood up separately.
- **One agent invocation.** A green spike proves the boundary for a single agent
  run, not the integrated system (ConPTY interactive I/O, LLM-API allowlist egress,
  per-task spin-up cost, Flow/Step orchestration all remain integration work).
```
