# Spike: agent / git privilege separation (standalone)

Status: **spike plan**, not built. Cold-readable. This proves the security model in
[`control-plane.research.md`](control-plane.research.md) **in isolation** — no A.Box
Flows, Steps, ConPTY, or billing. Build this *before* wiring any of it into the
orchestrator.

---

## 1. The single claim under test

> An untrusted "agent" process can edit files in a working directory, but **provably
> cannot** reach the git credential, the secret-at-rest, or the network — while a
> separate **control-plane** process commits the agent's output as the bot identity.

Everything else (LLM-ness, the real Flow, PTY plumbing) is orthogonal and explicitly
**out of scope** here. The spike succeeds when the attack matrix in §4 is all-green.

## 2. Why it's standalone-able

Every real part has a cheap stand-in that makes the boundary *easier*, not harder, to
test:

| Real part | Spike stand-in | Why it's fine |
|---|---|---|
| LLM agent (claude/codex) | a **deliberately adversarial script** (bash/python) | We *want* it to attack on purpose, not hope an LLM does |
| Control plane (.NET host) | a ~50-line script/process | We're testing the boundary, not the orchestrator |
| The agent's "work" | append a line to `hello.txt` | Trivial diff is the point |
| Remote | a local **bare** repo (GitHub only for the identity rung) | Separation logic needs no network |

What remains after the stand-ins *is exactly the thing under test*: env split,
principal/namespace isolation, egress block, file seam.

## 3. Scope

**In:** env scrub, secret-at-rest isolation, egress block, the file-only seam,
deterministic commit-as-bot, (rung 3) signed-commit + can't-self-approve.

**Out / deferred to integration:** ConPTY interactive I/O across the boundary; real
agents needing LLM-API egress (sandbox becomes *allowlist*-egress, not none);
per-task container/VM spin-up performance; Flow/Step orchestration. A green spike
proves the boundary for **one** agent invocation — it does **not** prove the
integrated system. Name this so the spike isn't oversold.

---

## 4. The attack matrix (the actual deliverable)

The spike's reason to exist. The adversarial "agent" runs each attack; each MUST land
on the stated result. This table is the pass/fail spec and transfers back into A.Box
as acceptance criteria.

| # | Attack the fake agent attempts | Required result | Enforced by |
|---|---|---|---|
| A1 | `env` / read process environment for a token | nothing secret present | env scrub / allowlist (§3.2 research) |
| A2 | read control-plane config, `~/.claude`, OS keyring | denied / not present | separate principal + ACL (§3.3) |
| A3 | `curl https://github.com` (or any host) | no route / blocked | egress block (§3.4) |
| A4 | `git push` from the working dir | no remote + no creds → fail | no `.git`/remote in sandbox (§5) |
| A5 | write & run a python exfil script (find secret → send it) | nothing to find, nowhere to send | A1+A2+A3 together |
| A6 | attempt to call / signal the control-plane process | no callable surface | one-way drive (§3.5) |
| A7 | control plane commits the agent's `hello.txt` diff | **succeeds**, authored as the bot | control plane holds identity (§4) |
| A8 | *(rung 3)* fabricate a commit "as the deterministic op" | unsigned → **rejected** | control-plane-only signing key (§4) |
| A9 | *(rung 3)* bot opens PR and self-approves | cannot approve | App/identity ≠ human code-owner (§4) |

A1–A7 are the core separation proof; A8–A9 are the identity/provability proof.

**Pass criteria:** every row observed at its required result, captured as logged
output (so the matrix is *demonstrated*, not asserted). Each red→green flip is the
evidence the corresponding boundary is real.

---

## 5. Rungs (each standalone; each ≈ a day)

Each rung strictly dominates the previous on isolation. Stop at the rung whose
guarantee you're satisfied with.

### Rung 1 — two processes, separate OS user (proves the *principle*)
- Control plane runs as user `cp`; agent runs as user `worker`.
- Working dir ACL'd to both; secret store + clone ACL'd to `cp` only.
- Agent spawned with a **cleared + allowlisted** environment.
- **Targets:** A1, A2, A4, A7. (No egress control yet → A3/A5 partial.)
- Cheapest; proves env-scrub + secret-at-rest isolation on one host.

### Rung 2 — container per task (the sweet spot)
- Agent runs in a container; working dir is a **bind mount / volume**; `--network none`
  (or allowlist later); image carries no secrets.
- Control plane on the host stages files in, runs the container, reads files out,
  commits.
- **Targets:** A1–A7 all green by construction. Most of the guarantee, modest effort.

### Rung 3 — identity & provability (orthogonal; can run in parallel)
- Real throwaway GitHub repo with the **ruleset** (require PR + code-owner + empty
  bypass) and **require-signed-commits**.
- Control plane authenticates as a **GitHub App** (or bot), mints a short-lived token,
  commits **signed** as the bot, opens a PR.
- **Targets:** A8, A9. Proves "can't self-approve" + "unsigned rejected."

### Rung 4 — microVM *(optional)*
- Only if rung 2 containment feels insufficient (Firecracker/gVisor, copy-in/out or
  virtio-fs). Strongest, highest ops cost. Same matrix, stronger A2/A5.

---

## 6. Suggested layout (when built)

Standalone repo or folder, *outside* A.Box:

```
spike-agent-isolation/
  control-plane/        # the ~50-line trusted runner: stage in → run agent → read out → commit
  fake-agent/           # the adversarial script: runs A1–A6, logs results
  attacks/              # one script per attack row, each prints PASS/FAIL
  rung1.{sh,ps1}        # wire up separate-user variant
  rung2.{sh,ps1}        # wire up container variant (compose / docker run --network none)
  results/              # captured matrix output per rung (the evidence)
  README.md             # this matrix + how to run each rung
```

The agent and control plane share **only** the working dir. No shared env, no shared
credential, no shared network.

## 7. Definition of done

- The §4 matrix is **all-green for the chosen rung**, with captured `results/` output.
- A short writeup of which rung was reached and what's **deferred to integration** (§3).
- The matrix lifted into A.Box as the acceptance criteria for the real
  control-plane/sandbox seam.
