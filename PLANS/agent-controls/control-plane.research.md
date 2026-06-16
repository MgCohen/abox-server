# Research: Control-plane / agent privilege split for git operations

Status: **research / target architecture**, not a committed decision. Cold-readable:
assumes no prior context. Scope is the *end state* of the A.Box orchestrator, not
the current Practical setup (which is a single-machine convenience documented in
[`README.md`](README.md)).

---

## 0. Context for a cold reader

**A.Box** is a .NET 10 orchestrator that drives coding-agent CLIs — `claude`,
`codex`, and (later) others — as child processes over **ConPTY** (Windows
pseudo-console), so it can run them under a human's *subscription* rather than
metered API keys. The orchestrator runs **Flows**: ordered **Steps**, some
deterministic (code) and some **agent Steps** (spawn an LLM CLI and let it work).

A realistic Flow:

```
1. git pull            (deterministic Step)
2. plan agent          (LLM)
3. review agent        (LLM)
4. work agent          (LLM)  ← edits files
5. review agent        (LLM)
6. git commit + PR     (deterministic Step)
```

Git touches the network and a credential; the LLM steps do not. **We want those
two concerns to never share a trust boundary.** Specifically:

1. The LLM agent must be **unable to perform git/remote operations itself.**
2. The git ops must run **as a dedicated machine identity** (today `ABox-Agent`),
   but as **deterministic code, never under LLM control.**
3. (Stretch) On a VM/container, the agent must be unable to **circumvent** any of
   this — e.g. by writing a Python script to find a token or reach the network.

The rest of this doc is how to achieve that, with the **environment split** as the
technical centerpiece.

---

## 1. The governing principle: zero ambient authority

> The LLM never holds, and can never reach, the authority to act on the outside
> world. Credentials and network-capable operations live in a deterministic
> **control plane** the LLM cannot influence or inspect.

"Ambient authority" = capability a process has just by existing in its environment
(an env var, an inherited file handle, a reachable keyring, an open network route).
Every requirement above is a corollary: we don't *forbid* the agent from misusing
git — we make sure the agent exists in a context where the ingredients of a git
push (credential + egress) are **simply not present.** Forbidding is a policy an LLM
can try to talk its way around; absence is not.

This reframes "block the agent" from *behavioral filtering* (enumerate and deny bad
actions — a losing game) to *capability removal* (the action has nothing to operate
on).

---

## 2. Two zones, one seam

```
┌─────────────────────────────────────┐        ┌──────────────────────────────────┐
│  CONTROL PLANE  (trusted, code)      │        │  WORKER SANDBOX (untrusted, LLM) │
│                                      │        │                                  │
│  • the A.Box orchestrator process    │        │  • one agent CLI (claude/codex)  │
│  • holds the bot identity + signing  │        │  • a working directory of FILES  │
│    key (in memory / OS secret store) │        │  • NO credential, NO secret env  │
│  • has network egress to GitHub      │        │  • NO .git remote / no git creds │
│  • runs the git Steps:               │        │  • NO network egress (or LLM-API │
│      git pull  ──── seeds files ───► │ ─────► │    endpoint only)                │
│      git commit/PR ◄── reads files ─ │ ◄───── │  • can ONLY read/write its dir   │
│  • brokers the agent's tool calls    │        │                                  │
└─────────────────────────────────────┘        └──────────────────────────────────┘
                     ▲                                          │
                     └──────────── the ONLY shared surface ─────┘
                              a working directory (files / a diff)
```

The Flow maps cleanly: Steps 1 & 6 run **in the control plane**; Steps 2–5 run **in
the worker sandbox**. They communicate by **files**, never by git and never by a
shared credential. The agent produces a *diff*; the control plane decides whether
and how to commit it.

### What "the control plane" actually is

The long-lived **A.Box .NET host process** (or a small privileged sidecar of it). It
is the thing that:

- Loads secrets *once* at startup from a secret store into **its own process
  memory** — not into a broadly-inherited environment. Candidates: a GitHub App
  private key file readable only by the orchestrator's user; Windows DPAPI blob;
  Windows Credential Manager under the orchestrator's service account; Azure Key
  Vault / cloud secrets manager.
- Owns the **real git clone** (the one with `origin` + credentials).
- Executes deterministic git Steps by spawning `git`/`gh` **child processes that it
  explicitly hands the credential to** (see §4).
- Spawns **agent** child processes with a **scrubbed environment** and a
  **restricted working directory** (see §3).
- Mediates any agent "tool" surface: the agent can request actions, but the broker
  only exposes a whitelist — and `commit`/`push` are **not on it.**

The agent, by contrast, is a *transient* child the control plane starts, feeds, reads
from, and tears down.

---

## 3. The environment split — the technical core

This is the part that earns the most care, because the default behavior of process
spawning **leaks** authority, and ConPTY does not change that.

### 3.1 Why the naïve spawn leaks

When any process creates a child, the child receives a **copy of the parent's
environment block** unless the parent supplies a replacement. On Windows,
`CreateProcess(..., lpEnvironment, ...)`: pass `NULL` → child inherits the parent's
environment verbatim; pass a custom block → child gets exactly that. ConPTY only
changes *how stdio is wired* (a pseudo-console instead of pipes); it does **not**
change environment inheritance — the PTY child still gets whatever environment the
spawning call specifies.

So if the orchestrator process itself has `GH_TOKEN` in its environment (e.g.
because *it* was launched that way) and then spawns the agent with default
inheritance, **the agent inherits `GH_TOKEN`.** That is the leak. The current
single-machine Practical setup is *exactly this shape*: the token sits in
`~/.claude/settings.json`'s `env`, Claude injects it into every subprocess it
spawns, and any of those subprocesses can read it. Fine for a trusted solo machine;
unacceptable when the subprocess is the untrusted worker.

### 3.2 Layer 1 — environment hygiene (necessary, not sufficient)

When spawning the agent, **construct an explicit, minimal environment** instead of
inheriting. In .NET terms, `ProcessStartInfo` starts from the parent environment;
you must **clear the secrets** rather than trusting a denylist:

```
var psi = new ProcessStartInfo("claude", ...);
psi.Environment.Clear();                       // start from nothing, not from us
foreach (var key in AgentSafeEnv.Allow)        // PATH, HOME/USERPROFILE, locale, TERM…
    psi.Environment[key] = Resolve(key);       // an explicit allowlist
// NO GH_TOKEN, NO GITHUB_TOKEN, NO GIT_* credential helper config,
// NO PATH entries that point at credential helpers or token files.
```

Rules that make this robust:

- **Allowlist, never denylist.** New secret-bearing vars appear over time; a
  denylist rots. Start empty, add only what the agent legitimately needs.
- **No credential-helper config reaches the agent.** Strip `GIT_CONFIG_*`,
  `GIT_ASKPASS`, `SSH_AUTH_SOCK`, `GH_*`. An `SSH_AUTH_SOCK` handle is ambient
  authority just like a token.
- **The agent's git config must define no usable helper.** Best: the agent has no
  `.git` at all (§5). If it does, its `HOME`/config points at a profile with the
  credential helper set to *nothing*.

This stops *accidental* and *in-band* leakage. It does **not** stop a determined
agent on a shared OS user from reading the token **at rest** — e.g. opening
`~/.claude/settings.json`, the orchestrator's config, or the OS keyring. Same user =
same filesystem and credential-store reach. Hence Layer 2.

### 3.3 Layer 2 — principal / namespace isolation (the real boundary)

Put the agent under a **different security principal** so the secret-at-rest is
*outside its reach*, enforced by the OS, not by our code.

Escalating options (pick by how much isolation you want vs. operational weight):

| Mechanism | What isolates the agent | Secret reachable by agent? | Weight |
|---|---|---|---|
| **Same process, scrubbed env** (§3.2) | nothing at rest | **Yes** (FS, keyring) | trivial |
| **Separate OS user** | file ACLs + per-user DPAPI/keyring | No (different user's store) | low |
| **Container** (Windows/Linux) | PID/mount/net namespaces | No (not in image/mounts) | medium |
| **microVM** (Firecracker/gVisor) | separate kernel | No (no shared kernel) | high |

The jump from row 1 → row 2 is the one that matters: under a **separate OS user**,
Windows encrypts each user's Credential Manager entries with **DPAPI keyed to that
user**, so the agent user literally cannot decrypt the orchestrator user's secrets;
file ACLs keep it out of the orchestrator's token files and `~/.claude`. From row 2
the secret is *absent from the agent's world*, which is what we want.

### 3.4 Layer 3 — network egress control

Even with no credential, an agent with network access is a liability (exfiltration
of source, reaching internal services, fetching a token from elsewhere). So:

- The agent's namespace/VM has **no default egress**. If the agent harness must
  reach the LLM provider API, allow **only** that endpoint (and any package
  registry it genuinely needs), nothing else — especially **not** `github.com`.
- Egress control is the **single highest-value control**: it neutralizes both
  exfiltration *and* unauthorized pushes regardless of what code the agent writes.
  The git push happens from the **control plane's** network context, which *does*
  have GitHub egress, and which the agent is not in.

### 3.5 How the agent cannot reach *back into* the control plane

Isolation must be one-directional: the control plane drives the agent, but the agent
can't invoke the control plane's privileged code.

- **No callable surface.** The orchestrator communicates with the agent only through
  the PTY's stdin/stdout (it *writes prompts, reads output*). The agent has no RPC
  handle, socket, or function pointer into the orchestrator. It cannot call
  `Commit()`; it can only emit text the orchestrator chooses how to interpret.
- **Brokered tools only.** If the agent uses a tool/MCP protocol, the orchestrator
  is the broker and exposes a **whitelist**; `git`/`push`/`commit` are simply not
  registered tools. Requests for anything off-list are refused by code, not by
  prompt.
- **No introspection.** Different principal ⇒ the agent can't `ptrace`/attach to the
  orchestrator process, read its `/proc/<pid>/environ`, or open its file handles.
- **The seam is data, not control.** The only thing crossing the boundary is the
  working directory's *files* (§6) — inert data, reviewed downstream (§7).

---

## 4. Running the git op deterministically, under the bot identity

The commit/PR Step is **control-plane code**. It holds the identity; the agent never
sees it. Two things people conflate:

- **Author/committer** = `ABox-Agent` — *cosmetic metadata* stamped on the commit.
- **Push credential** = what GitHub actually authenticates and enforces on — held
  and presented by the control plane only.

The deterministic op spawns `git`/`gh` with the credential placed in **that child's**
environment (or via a credential helper that reads the secret store), with CWD = the
**control-plane clone** — the one the agent never had.

### Recommended end-state identity: a GitHub App (not a PAT)

- The orchestrator authenticates as a **GitHub App installation** and **mints a
  short-lived (~1 h) installation token per operation**, scoped to exactly the repo
  and permissions needed. No long-lived secret sits around to steal.
- Apps **can't sign in to the web UI and can't stand in for a human code-owner
  review** → the "agent can't self-approve its own PR" guarantee becomes
  *structural*, not policy.
- Add **commit signing** with an SSH/GPG key held *only* by the control plane, and a
  branch-ruleset rule **"require signed commits."** Now a landed commit is
  *cryptographically provably* the product of the deterministic op: anything the
  agent might fabricate would be **unsigned and rejected at the server.** This is the
  concrete proof of "git ran as code, not as the LLM."

### The trust seam: why blindly committing agent output is still safe

The deterministic op does **not** need to trust the file content. Bad output is
caught by **downstream gates**, which are independent of the agent:

1. the **review agent** Step(s),
2. **CI** (`build-test`, `policy-guard`, etc.),
3. the **owner-approval branch ruleset** (require PR + 1 approval + code-owner review
   + empty bypass list).

The mechanical commit and the trust gate are deliberately *different things*: the op
is dumb on purpose; safety comes from the gates a change must survive to land.

---

## 5. The cleanest "agent can't use git": there is no git in the sandbox

The strongest form of requirement (1) is also the simplest. **Don't give the agent a
repository at all.** Give it a plain working directory — the files, with **no `.git`,
no remote, no config.** Then:

- The agent *cannot* run git operations because there is no repo and no remote — not
  blocked by policy, absent by construction.
- After the agent finishes, the control plane **diffs the directory against the base
  snapshot** it seeded, and applies/commits that diff into its own clone.

If the agent genuinely needs history/blame for context, give it a **read-only clone
with no remote and no credential helper**, still inside the no-egress sandbox — it
can read history but has nowhere to push and nothing to push with.

---

## 6. How files are shared across the boundary

The seam is a working directory; the mechanism depends on the isolation tier:

- **Separate OS user:** a working dir whose ACL grants *both* the orchestrator user
  and the agent user access to **that directory only**; the agent user has no ACL on
  the orchestrator's secret store or clone. Shared by path.
- **Container:** the working dir is a **bind mount / named volume** mounted into the
  agent container. The container sees *only* the mount — none of the host FS, none of
  `~/.claude`, no keyring. The orchestrator reads/writes the same path on the host.
  The volume is the entire shared surface.
- **microVM:** share via a **virtio-fs mount**, or **copy-in/copy-out** over a control
  channel (orchestrator stages base files in, runs the agent, copies the result out).
  No shared kernel; strongest seam.

In all cases: **files in, files out.** The base files are staged by the control plane
(post-`git pull`); the modified files are read back by the control plane (pre-commit).
Git, credentials, and network never cross.

---

## 7. Threat model — what each boundary buys

| Threat | Defended by |
|---|---|
| Agent runs `git push` to land code unreviewed | No remote/creds in sandbox (§3,§5) + server ruleset (§4) |
| Agent reads the token from env | Env scrub / allowlist (§3.2) |
| Agent reads the token at rest (keyring, config files) | Separate principal/namespace (§3.3) |
| Agent writes a Python script to grab a secret | Nothing to grab — secret absent from sandbox (§3.3) |
| Agent writes a script to exfiltrate source / reach a token elsewhere | No egress / egress allowlist (§3.4) |
| Agent calls back into the orchestrator to force a commit | No callable surface; brokered tools exclude git (§3.5) |
| Agent fabricates a commit "as the deterministic op" | Signing key is control-plane-only; require-signed-commits rejects it (§4) |
| Agent self-approves its own PR | App/identity ≠ a human code-owner; can't approve (§4) |

### The stretch goal, answered

> Can a VM stop the agent from going around security with, e.g., a Python script?

**Yes — but the mechanism is not "detect malicious scripts" (unwinnable). It is
structural removal:** (1) the credential is **not in the VM** (control plane is
outside it), (2) there is **no egress** to send anything anywhere or to reach GitHub,
(3) the sandbox is **ephemeral + unprivileged** (clean image per task, least-priv
user, seccomp/AppArmor or a microVM). A script the agent writes is free to run and
finds **nothing to steal and nowhere to send it.** The agent's ability to author
arbitrary code becomes harmless because it has no ambient authority to exercise.

---

## 8. Implementation spectrum (cheapest → strongest)

You can adopt this incrementally; each row strictly dominates the one above on
isolation:

1. **Env-scrubbed same-process spawn** — allowlist env for agent children; keep
   secrets out of their environment. (Stops accidental leak; not a security boundary
   on a shared user.)
2. **Separate OS user for agents** — agent runs as `abox-worker`; secret store and
   clone ACL'd to the orchestrator user only; working dir shared by ACL.
3. **Container per task** — agent in a container, working dir bind-mounted, **no
   network** (or LLM-API egress only), image carries no secrets.
4. **microVM per task** — Firecracker/gVisor; copy-in/out or virtio-fs; strongest
   containment, hardest ops.

Identity track (orthogonal, adopt early): **PAT → GitHub App + short-lived tokens +
required signed commits.**

---

## 9. Open questions / decisions to make

- **Does the agent need git history at all?** If no → §5 "no repo in sandbox" is the
  default and simplifies everything. If yes → read-only clone, no remote.
- **Where does the control plane run relative to the agent?** Same host (separate
  user) vs. agent-in-container/VM with orchestrator on host. Determines the file-seam
  mechanism (§6).
- **GitHub App vs. machine-user PAT** for the deterministic identity. App is the
  target; PAT is the stopgap. (Cross-provider note: identity lives in the control
  plane, so this is provider-agnostic — Claude/Codex/other agents never hold it.)
- **Signing**: SSH-signing (simpler key mgmt) vs. GPG; and whether to turn on
  *require signed commits* in the ruleset as the provability backstop.
- **Egress policy**: do the agent harnesses *require* outbound API calls (hosted
  LLM), or can they run fully offline? Determines whether the sandbox is no-egress or
  allowlist-egress.

---

## 10. One-paragraph summary

Split the system into a **deterministic control plane** that holds the GitHub
identity (ideally a GitHub App minting short-lived, signed-commit-capable tokens) and
performs all git/network operations, and an **untrusted worker sandbox** where the
LLM agent edits **files only** — with **no secret env (scrubbed allowlist), no
secret at rest (separate OS principal/namespace), and no network egress**. The two
zones share exactly one surface: a **working directory** staged in after `git pull`
and read back before `git commit`. The agent can't use git because git, its
credential, and its egress are simply **absent** from its world; the git op runs
deterministically as the bot because the **control plane** — which the agent cannot
inspect, call, or reach — is the only place the identity exists. On a VM, the same
split makes evasion moot: there is nothing in the sandbox to steal and nowhere to
send it.
