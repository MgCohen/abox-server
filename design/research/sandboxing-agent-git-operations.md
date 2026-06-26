# Credential-Absent Agent Editing: Orchestrator / Executor Design (v3)

**Status:** Design proposal (v3 — incorporates second-round security-review fixes) · **Target:** .NET 10, Windows-first (macOS secondary) · **Date:** 2026-06-19

> **What changed since v2.** The second-round review **confirmed v2's fixes are correctly closed and they stay unchanged**: the clean-state invariant (token-bearing git runs in an orchestrator-owned `$PUSH` with orchestrator-authored config, only file *contents* copied from `$EDIT`); credential.helper / env-inheritance handled via a named-pipe askpass; `ScopedToken` memory honesty (no scrubbing claim); the Serilog out-of-band-token discipline + log-sink round-trip test; first-clone / lifecycle / concurrency; and branch-protection scope (force-push + deletions + no-bypass; `contents:write` vs `pull_requests:write`).
>
> **But the review found the headline code-execution class was only *partially* closed.** The weak link is **the content-transfer step itself** ("copy files, exclude `.git`"). Execution can be smuggled back in by means that do **not** live in `.git/config` and do **not** match the name `.git`:
> 1. **A buried *bare* repository** (just `HEAD` + `objects/` + `refs/` + `config`, no `.git` dir) committed into `$EDIT`. `robocopy /XD .git` copies it; then `git add -A` in `$PUSH` *discovers* it and runs its `core.fsmonitor` payload **inside the process about to hold the token** (the GHSA-9ccr-r5hg-74gf "nested bare repository via `core.fsmonitor`" class). **Fix 1.**
> 2. **Case-fold / NTFS-shortname / symlink `.git` smuggling.** Both target OSes are case-*insensitive* by default, so excluding `.git` by *name* does not exclude `.GIT`, `.Git`, the 8.3 short name `GIT~1`, or a symlink that *resolves* to `.git` (the CVE-2024-32002 class). **Fix 2 — name-based exclusion is insufficient; transfer the agent's *tracked tree*, not a raw file mirror.**
> 3. **The single-use askpass pipe** breaks git's *legitimate* multi-call askpass (username, then password, then retries). **Fix 3 — serve for the lifetime of the one git invocation, ACL-pinned.**
> 4. **TOCTOU** between "confirm no agent live" and `DoGit`. **Fix 4 — acquire the per-(user,workspace) lock first, then check-and-act atomically under it.**
> 5. **Transfer fidelity** — a raw mirror + `git add -A` drops `.gitignore`-ignored force-adds and loses mode-bit/symlink intent. **Fix 5 — the commit is "the agent's tracked working-tree state."**
>
> Smaller corrections: v2 §6.2 "option 2" (`--work-tree=$EDIT --git-dir=$PUSH/.git add`) is **removed/marked DO-NOT-USE** (it re-reads `$EDIT`'s attributes and re-discovers its embedded repos); the inert-`.gitattributes` claim is clarified (inert because no driver *and* no `filter.<x>.required=true` — itself a config key `$PUSH` doesn't set); the setup clone uses `-c safe.directory=$PUSH` (specific path, not `*`); the CVE id is presented honestly (the verified advisory is **GHSA-9ccr-r5hg-74gf**); and §9's "App not in bypass list" becomes a **programmatic assertion** at flow start.
>
> The architecture — orchestrator/executor split, first-class Operations, the hardening ladder, the clean-state invariant — is unchanged. v3 hardens the *transfer seam* so the clean-state invariant cannot be re-opened from the agent's tree.

> **What changed since v1 (retained for context).** v1's core claim — that running the agent and the token-bearing git in *separate, non-overlapping processes* makes credential capture impossible — was wrong: **temporal separation of *processes* is not separation of *state*.** The agent's `.git` (config, tracked `.gitattributes`, index) **persists** into the token-bearing `DoGit` turn, and **git executes code defined in config and attributes — not just hooks.** `--no-verify` + `core.hooksPath=` disables hooks and nothing else. It does not touch `core.fsmonitor`, `filter.<x>.clean/.smudge/.process`, `core.sshCommand`, `core.pager`, `diff.external`, `credential.helper`, `[include]`/`includeIf`, `gpg.program`, or aliases — every one of which can run an agent-chosen command *inside the token-bearing git process or its children*. v2 fixed this by inverting control (clean-state invariant). v3 closes the transfer-step residue.

---

## TL;DR / Decision Summary

We let provider-agnostic autonomous coding agents (Claude Code, OpenAI Codex CLI, any CLI agent) edit code across long, multi-turn, multi-agent flows, while keeping git **write** operations (commit-to-remote / push) out of the agent's reach. The agent process **never has a git credential in its environment**, and — the part v1 missed — **the token-bearing git never reads configuration, attributes, or an index that the agent authored, and never *discovers* a repository the agent buried in its tree.**

The mechanism is a **controller/worker split** plus a **clean-state invariant**:

- **Orchestrator** — trusted coordinator. Owns the master credential (a GitHub App private key). Knows the flow. Mints short-lived scoped installation tokens. Dispatches first-class **Operations**. **Never runs git against the agent's repo itself.**
- **Executor** — runs **Operations** against **one persistent workspace** (a single working tree + `.git` alive for the whole flow). It invokes the agent CLI (no token in env) and invokes git (with a token); the two never run concurrently.
- **Clean-state invariant (the v2 centerpiece, v3-hardened)** — the *token-bearing* git does not run in the agent's `.git`. It runs against an **orchestrator-owned `.git`** (separate `GIT_DIR`/worktree/clone) whose `config` the orchestrator authored, in a **clean environment** (`GIT_CONFIG_GLOBAL`/`GIT_CONFIG_SYSTEM` neutralized, no inherited `GIT_*`), **with repo discovery pinned and bare-repo discovery refused** (`-c safe.bareRepository=explicit`, explicit `--git-dir`/`--work-tree`, `GIT_CEILING_DIRECTORIES`). Only the agent's **tracked tree content** crosses over — never its `.git`, never a buried repo, never a path that case-folds or symlink-resolves to `.git`.

The single guarantee everything hangs on is no longer "credential absence during agent turns" alone (necessary but insufficient). It is **two** invariants held together:

| Invariant | What it stops |
|---|---|
| **I1 — credential absence during agent turns** | the agent reading the token from its own env / a sibling |
| **I2 — clean git state during token-bearing turns** | the agent's `.git` config/attributes/index — *or a repo it buried in the tree* — running code in the process that holds the token |

| Operation | Token in process env? | Agent running concurrently? | Runs against agent's `.git`? |
|---|---|---|---|
| `RunAgent` | **No** | (it *is* the agent) | yes — harmless, no token present |
| `DoGit` (stage/commit/push) | **Yes** (short-lived, scoped) | **No** | **No — orchestrator-owned clean `.git`, discovery pinned** |

**Why I2 is the robust control, not key-disabling.** A malicious in-tree `.gitattributes` line like `* filter=evil` is **inert** if `filter.evil.clean`/`.smudge`/`.process` is **not defined in config** *and* `filter.evil.required=true` is **not** set (and `filter.<x>.required` is itself a config key — `$PUSH`'s orchestrator-authored config sets none of these, so an in-tree `.gitattributes` cannot flip `required` on by itself). The same is true for fsmonitor, sshCommand, the credential helper, the pager, the external diff driver — all of them are *config-defined commands*. The orchestrator's `.git/config` defines **none** of them (only the remote URL). So the entire config class is neutralized at the source by controlling *config*, without ever having to predict driver names. **v3 adds the missing half:** config control is necessary but not sufficient, because a buried *bare repo* (Fix 1) or a case-folded/symlinked `.git` (Fix 2) can re-introduce an attacker-controlled `config`/hooks via *discovery* of state in the tree. v3 closes that by pinning discovery and transferring the tracked tree rather than mirroring files.

This meets the stated bar. An **optional hardening ladder** (separate low-priv OS user for `DoGit`; container/WSL2/Sandbox for the agent; egress control) upgrades the process boundary into a real OS boundary when the bar rises. And a **server-side backstop** (branch protection: required reviews **plus** restrict-force-push **plus** no-app-bypass, asserted programmatically at flow start) holds even if every local control fails.

We drop v1's "structurally impossible" language where it overreached. The honest framing: **given the clean-state invariant *and a transfer step that imports only the agent's tracked tree under pinned, bare-refusing discovery*, the token-bearing repo contains zero agent-authored config, attributes, index, or buried repository — so there is no agent-controlled code path in the token-bearing process — backstopped by branch protection.**

---

## 1. Threat Model (this drives everything)

**Adversary we defend against:** a *semi-trusted* agent that **improvises to route around protections mid-task.** Concretely it might:

- call `git push` directly, by absolute path, or via a language runtime;
- write a shell/PowerShell script and run it;
- read its own environment block, scan the workspace, read `.git/config`, look for `~/.git-credentials` or a credential helper;
- try to set its own remote and push there;
- **plant config/attributes that turn the *orchestrator's own* later git command into an exploit** — e.g. commit a `.gitattributes` with `* filter=x`, or set `core.fsmonitor`, hoping the token-bearing `DoGit` will trigger it (the vector v1 missed; v2's I2 is the config answer);
- **bury a whole repository in its tree so the orchestrator's transfer step smuggles it across.** A *bare* repo is just `HEAD` + `objects/` + `refs/` + `config` (no `.git` directory), so a name-based `/XD .git` does not exclude it; once in `$PUSH`, `git add -A` *discovers* it and runs its `core.fsmonitor`. Or it commits `.GIT`, `GIT~1`, or a symlink resolving to `.git` — none excluded by name on a case-insensitive FS. (These are the vectors v2's transfer step missed; v3's Fix 1 + Fix 2 are the answer.)

**Adversary we explicitly do NOT defend against (out of scope at this bar):**

1. **Persistent background watcher** that captures a credential arriving *later* in a sibling process (a daemon polling other processes' env/memory until a `DoGit` fires).
2. **Deliberate process-memory spying** on a concurrent sibling (reading another process's memory/handles).
3. **Code exfiltration to an agent-controlled remote** when the agent has open network egress — a *different* problem ("agent leaks MY code") than the one solved here ("agent writes to MY repo"). Out of scope unless egress control is added (Rung 3 / §9).

**Why this relaxation is the point.** Because we exclude the persistent-watcher and memory-spy cases, **I1 + I2 are sufficient**: during the agent's turn no token exists in any process it can read; during the token-bearing turn no agent runs *and* git touches no agent-authored state *and discovers no agent-buried repo*. We don't need a heavy OS sandbox to meet this bar — the sandbox is an *optional* upgrade for when the bar rises to include (1) or (2).

State this honestly to anyone relying on the system: **baseline defends against improvisation, not against a determined implant.**

---

## 2. The two boundaries — what isolation actually buys, and what it does not

v1 framed the boundary as "separate process AND non-overlapping in time." That is **half** the boundary (I1). It is necessary — but it does not stop the agent's *persistent state* from running code in the token-bearing process. There are two boundaries, and you need both:

**Boundary A — the credential boundary (I1, temporal + environmental).**
1. The executor spawns the **agent CLI** with an environment block it constructs, **without** the token. A child cannot read a variable that was never set.
2. The agent does its turn — edits files, reads files, runs builds, runs *read-only* git (`diff`/`status`/`log`). Nothing sensitive is in the tree or its env.
3. To push, the executor spawns a **different** child — git — and *that* invocation carries the token. The two **never overlap in time**.

Under §1's exclusions, Boundary A stops the agent from reading the token. But it says nothing about *what code runs* once the token-bearing git starts.

**Boundary B — the execution-provenance boundary (I2, state separation).**
Git is a *config-driven program*. Many config keys name an external command git will execute: `core.fsmonitor`, `core.sshCommand`, `core.pager`, `core.editor`, `filter.<x>.clean/.smudge/.process`, `diff.<x>.command` / `diff.external`, `merge.<x>.driver`, `credential.helper`, `gpg.program`, `[include]`/`includeIf`, and aliases (`alias.* = !sh -c ...`). Several of these are **triggered by ordinary write operations the orchestrator performs**:
- a committed **`.gitattributes`** with `* filter=x` makes **`git add`** invoke `filter.x.clean` — *the orchestrator's own staging is the trigger* — *if* `filter.x.*` is defined in config (or `filter.x.required=true` forces a hard failure that some flows turn into a driver call);
- **`core.fsmonitor`** runs on virtually every operation that scans the work tree — **and is read from the config of *whatever repo git discovers*, including a bare repo buried in the tree** (GHSA-9ccr-r5hg-74gf);
- **`credential.helper`** is invoked by `git push` over HTTPS and is handed the **live token** — a token-capture vector, not just code-exec.

If any of these is defined in a `.git/config` the **agent** wrote — *or in the `config` of a bare repo the agent buried in the tree that git then discovers* — then **the agent's code runs inside the process that holds the token** (or, for `credential.helper`, git *hands* the agent's helper the token). `--no-verify` and `core.hooksPath=` do **not** cover any of these — they cover hooks only.

Boundary B is the clean-state invariant: **the token-bearing git uses an orchestrator-authored config and environment, against a `.git` the agent never wrote to, with repo discovery pinned so it cannot wander into a buried bare repo, and with the tree imported as tracked content rather than a name-filtered file mirror.** Implementation in §6.

> **One-line statement of the boundary:** *the agent may control file contents; it must never control the configuration, attributes, index, or *any discoverable repository* that the token-bearing git evaluates.*

### 2.1 The optional hardening ladder

Each rung is independent; you don't need all of them.

- **Rung 0 (baseline):** I1 + I2 — non-overlapping processes, token only in `DoGit`'s env, clean-state invariant on the token-bearing git (clean config + pinned discovery + tracked-tree transfer). *Meets §1.*
- **Rung 1 — run `DoGit` as a separate low-privilege OS user.** A *concurrent* same-box agent (same user) can introspect a sibling's env on some OSes; a separate user closes that (env blocks are readable only by the owning user + admins). Windows: `CreateProcessAsUser` + a custom environment block. macOS: a service user via `launchd`/`sudo -u`. Closes the env-read path of §1 case (2). **Caveat: reading another user's process env requires same-user *or* admin/`SeDebugPrivilege` — so an *admin* agent defeats this rung.**
- **Rung 2 — confine the agent itself** in a container / WSL2 / Windows Sandbox with no view of the host process table. Closes the persistent-watcher case (1) for the host.
- **Rung 3 — add egress control** (deny outbound except an allowlist) to address exfiltration (§9). Orthogonal to credentials.

**Baseline already meets the stated bar.** Climb only when the bar moves.

---

## 3. Component Architecture

```
                    ┌──────────────────────────────────────────────┐
                    │            ORCHESTRATOR (trusted)             │
                    │                                               │
                    │  • holds GitHub App private key (master)      │
                    │  • knows the flow (step sequence)             │
                    │  • mints short-lived scoped installation tok. │
                    │  • AUTHORS the clean .git/config (remote only)│
                    │  • decides what to stage/commit               │
                    │  • dispatches Operations                      │
                    │  • DOES NOT run git in the agent's .git       │
                    │  • DOES NOT run agents                         │
                    └───────────────────┬──────────────────────────┘
                                        │ Operation  (token, if any, passed OUT OF BAND)
                                        ▼
                    ┌──────────────────────────────────────────────┐
                    │             EXECUTOR (worker)                 │
                    │                                               │
                    │  • runs Operations against the workspace      │
                    │  • spawns agent CLI   (NO token in env)       │
                    │  • spawns git in the CLEAN .git (token in env)│
                    │    with discovery PINNED + bare-repo REFUSED  │
                    │  • never persists / logs the token            │
                    │                                               │
                    │   ┌─────────────────────┐  ┌───────────────┐  │
                    │   │ AGENT EDITING TREE  │  │ CLEAN PUSH    │  │
                    │   │  work tree + agent  │  │ REPO          │  │
                    │   │  .git (agent may    │  │ orchestrator- │  │
                    │   │  write config/attrs,│  │ owned .git,   │  │
                    │   │  bury repos)        │  │ config author-│  │
                    │   │  NO token ever      │  │ ed by orch.   │  │
                    │   │                     │→ │ token ONLY    │  │
                    │   └─────────────────────┘  │ here, no agent│  │
                    │     TRACKED TREE imported  │ discovery     │  │
                    │     (not a name-filtered   │ pinned        │  │
                    │      file mirror)  ──────> └───────────────┘  │
                    └──────────────────────────────────────────────┘
```

**Trust boundaries:** (a) credential *ownership* — only the orchestrator holds the master key; (b) credential *presence* — the token reaches only the clean-push git process, never the agent's env; (c) execution *provenance* — the token-bearing git evaluates only orchestrator-authored config/attributes/index **and discovers no agent-buried repository**. The executor is the small, auditable piece that must keep (b) and (c) true.

---

## 4. The Operation Model

An **Operation** is a first-class, logged unit of work: type, inputs, result, commit SHA, timestamps, status — all recorded. The **token is never a field of any recorded or dispatched object.** It is passed to the executor **out of band**, as a separate argument, and is **un-serializable by type** (§4.2).

### 4.1 .NET shape (concept is portable)

```csharp
// The persisted, auditable record. This is what hits the log/store.
public sealed record OperationRecord(
    Guid           Id,
    OperationKind  Kind,            // RunAgent | GetDiff | WriteFile | DoGit | RunBuild | RunTests
    string         WorkspaceId,
    IReadOnlyDictionary<string,string> Inputs,   // agent id, paths, git sub-action, message...
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    OperationStatus Status,         // Pending | Running | Succeeded | Failed
    string?        ResultSummary,   // diff stat, build outcome, etc.
    string?        CommitSha);      // set by DoGit commit, else null

// The dispatch envelope. NOTE: it has NO token field. The token is NOT a
// property of anything loggable or dispatched — see §4.2 for why.
public sealed record OperationRequest(OperationRecord Record);

// Passed to the executor as a SEPARATE argument, never inside a request/record:
//   executor.Execute(OperationRequest request, ScopedToken? token);
```

### 4.2 Token handling — honest version

v1 made three claims that do not survive review. The corrected story (confirmed correct by the second review — keep as-is):

**(a) `ScopedToken` is a sealed *class*, not a struct, and disposing it does NOT scrub memory.**
v1 called it a "struct ... so the secret stays off the heap" and implied `Dispose()` scrubs the value. Both are wrong. It holds a `System.String`, which is a heap object, **immutable**, freely **copied** by the runtime, and possibly **interned**. Setting the field to `null` only drops *one* reference; the underlying character data lingers until GC, and any copy made by `string.Concat`, an interpolation, or `ProcessStartInfo` lingers independently. **Do not claim memory scrubbing.** `Dispose()` here is *hygiene* (drop the reference, mark disposed so further `Reveal()` throws), not a memory control.

If you want *real* scrubbing, hold the secret as `byte[]` / `Span<byte>` you can zero (`CryptographicOperations.ZeroMemory`) and only materialize a `string` at the moment of injection. That is more code; mention it as the upgrade, do not pretend the `string` version achieves it.

```csharp
public sealed class ScopedToken : IDisposable
{
    private string? _value;
    public ScopedToken(string value) => _value = value;
    public string Reveal() => _value ?? throw new ObjectDisposedException(nameof(ScopedToken));
    // Hygiene only. Drops a reference + blocks re-Reveal. Does NOT scrub the string from memory.
    public void Dispose() => _value = null;
    public override string ToString() => "[redacted scoped token]";
}
```

**(b) The real audit control is *out-of-band passing*, not `[JsonIgnore]` — because `[JsonIgnore]` does not stop Serilog.**
`[JsonIgnore]` is honored by `System.Text.Json`, but a structured-logging call like `log.Information("dispatching {@Request}", request)` uses Serilog's **destructuring**, which **reflects over public properties** and will happily serialize a `Token` property *even though `System.Text.Json` would skip it*. The robust fix is **typological, not attribute-based**: the token is **not a property of any object that can reach a log sink.** It is a separate argument with a redacting `ToString()` and no public value-bearing property. There is no `{@x}` that can reach it because it is not inside any `x` that gets logged.

> **Wire-rulebook test (recommended):** round-trip an `OperationRequest` (and the whole `OperationRecord`) through the **actual configured log sink** — Serilog with its real destructuring policy — and assert the rendered output contains no substring matching a token shape (`ghs_`, `ghp_`, or the literal minted value). Do the same for the JSON audit serializer. This is the test that catches a future refactor that re-adds a token property.

**(c) The orchestrator constructs the token only for `DoGit`.** Every other operation simply has no token argument. There is no code path from the token to disk because the token never enters a serializable type.

> **Persistence rule (one line):** the store and the log sink see `OperationRecord`/`OperationRequest` only; the `ScopedToken` is an out-of-band argument, disposed in a `finally` at the end of `DoGit`.

---

## 5. Token Minting (orchestrator-owned master key → short-lived scoped tokens)

**Mechanism: GitHub App installation access tokens.** The clean realization of "orchestrator owns a key, mints short-lived scoped tokens, fully automatable":

- The **master credential** is the GitHub App's **private key** (RSA). It lives only on the orchestrator (DPAPI / Keychain / a secret store / ideally a managed identity so the raw PEM isn't on disk).
- The orchestrator signs a short-lived **JWT** (RS256, `exp` ≤ 10 min — GitHub's hard limit) authenticating *as the app*.
- It exchanges the JWT for an **installation access token** via `POST /app/installations/{installation_id}/access_tokens`. The installation token **expires after ~1 hour**, and is **narrowed to specific `repositories` and `permissions`** at mint time.

The long-lived secret never leaves the orchestrator; what flows to a `DoGit` is a **time-boxed, repo-scoped, permission-scoped** token, useless an hour later.

### 5.1 Scopes — push vs PR-open (v1 contradiction, reconciled)

v1 minted `contents:write` only but claimed tokens could "open PRs." **That is wrong.** On GitHub:

- **Pushing commits / branches** requires **`contents: write`**.
- **Opening or modifying a pull request** requires **`pull_requests: write`** — a *separate* permission. `contents:write` alone cannot open a PR.

Choose per flow:

| You want `DoGit` to… | Mint permissions |
|---|---|
| push to a branch only (PR opened out-of-band by a human or a different, more-privileged path) | `{ contents: "write" }` |
| push **and** programmatically open the PR | `{ contents: "write", pull_requests: "write" }` |

Recommendation: mint **`contents:write` only** for the per-push `DoGit`, and open the PR from a **separate, explicitly-scoped operation** (its own short-lived token with `pull_requests:write`) so the token that ever sits in a git child process can only push, never manipulate PRs. Keep the blast radius minimal per operation.

`metadata: read` is implicitly granted and needed for most calls; that is fine.

### 5.2 Minting flow (pseudocode)

```csharp
// (1) Sign a JWT as the App. exp <= 10 minutes (GitHub hard limit), RS256.
string jwt = JwtBuilder()
    .Issuer(appId)                                   // "iss" = GitHub App ID
    .IssuedAt(now.AddSeconds(-30))                   // clock-skew slack
    .Expiration(now.AddMinutes(9))
    .SignWith(RS256, appPrivateKeyPem);              // the MASTER key — orchestrator only

// (2) Exchange JWT -> installation access token, NARROWED to repo + permission.
var resp = await http.PostJson(
    $"https://api.github.com/app/installations/{installationId}/access_tokens",
    bearer: jwt,
    body: new {
        repositories = new[] { "abox-server" },      // scope: just this repo
        permissions  = new { contents = "write" }    // scope: push only (add pull_requests:"write" only if opening PRs)
    });

return new ScopedToken(resp.token);   // "ghs_…", ~1h TTL, scoped; ephemeral, never persisted/logged
```

Mint **just before** dispatching `DoGit`; let it expire; don't cache across idle gaps. Re-mint per push — cheap, and it shrinks the window.

### 5.3 How git consumes the token — and the inheritance trap

Two issues: (i) keep the token out of `.git/config` / command line; (ii) **keep it from being inherited by git's child processes.**

**The inheritance trap (v1 missed this).** `git push` over HTTPS **spawns children** — `git-remote-https`, and whatever `credential.helper` / fsmonitor / filter commands are configured. **Every child inherits the parent's environment.** So if the token sits in `ABOX_GIT_TOKEN` in the git process's env, then *any* command git spawns reads it directly from its own environment — **no sibling-spying needed.** Under the clean-state invariant (§6) there are *no* hostile config-defined children, which is the primary defense. But defense in depth says: **don't put the token in an inheritable env var at all if you can avoid it.**

Forms, best to worst:

**A. Askpass that reads from a channel git's descendants can't see, not an inherited env var (preferred).**
Point `GIT_ASKPASS` at a tiny helper that reads the token from a **source the helper opens itself** — a named pipe, an inherited handle, or stdin — rather than from an env var that all of git's descendants also see. The token is delivered only to the askpass helper.

```text
# askpass helper (conceptual): connect to a per-launch named pipe and print the token.
# Windows: \\.\pipe\abox-git-<guid> ; macOS: a 0600 unix socket / FIFO in a private temp dir.
# The pipe is created by the executor and SERVES THE TOKEN FOR THE LIFETIME OF THE ONE
# git invocation — answering EVERY askpass call from that git process tree — then is
# torn down in finally. (See "Fix 3" below: it is NOT a single-connection pipe.)
```

```csharp
psi.Environment["GIT_ASKPASS"]         = askPassPath;     // path quoted if it contains spaces
psi.Environment["GIT_TERMINAL_PROMPT"] = "0";             // never fall back to an interactive prompt
psi.Environment["ABOX_GIT_PIPE"]       = pipeName;        // a NAME, not the secret — safe to inherit
// the token is served over the pipe to the askpass helper(s) of THIS git invocation, then torn down.
```

This way **no descendant of git ever has the token in its environment.** The askpass helper gets it; nothing else does.

**Fix 3 — serve the token for the LIFETIME of the one git invocation, not for one connection.**
Git calls `GIT_ASKPASS` **more than once** per push: once for the **username**, once for the **password**, and **again on retry / HTTP redirect** (and possibly interleaved when sub-processes run in parallel). v2's "accept ONE connection then close" **hangs or fails the second legitimate call.** The correct lifetime is: the executor's pipe/socket **answers every askpass prompt originating from this single `git push`/`clone` process tree**, for as long as that git invocation is running, then is **torn down in `finally`** when the invocation exits — *not* literally one connection. Concretely on Windows, keep a `NamedPipeServerStream` loop (`WaitForConnection` → write token → `Disconnect` → loop) bounded by the lifetime of the spawned git process, and dispose it in the `finally` that also disposes the token. On macOS, a `0600` FIFO/socket served the same way.

**ACL pinning (also Fix 3).** Pin the channel to the identity that runs the token-bearing git:
- **Windows named pipe:** create it with a `PipeSecurity` ACL granting access **only to the `abox-git` account's SID** (and `LocalSystem`/admin as needed), denying everyone else — so a concurrent same-box process under a different account cannot connect and harvest the token. Use `NamedPipeServerStreamAcl.Create(..., pipeSecurity)`.
- **macOS socket/FIFO:** `0600`, owned by `_aboxgit`, in a private (`0700`) temp dir.

**B. Askpass reading a transient env var (simpler, weaker).** `GIT_ASKPASS` → helper that echoes `%ABOX_GIT_TOKEN%`. Simple, but the token **is** in git's env and is therefore inherited by every child. **Only acceptable in combination with the clean-state invariant** (so there are no hostile children) — and even then, prefer A.

**C. Inline HTTPS URL** `https://x-access-token:<TOKEN>@github.com/owner/repo.git` — token lands in process args and risks `.git/config`. **Avoid.** If used, set the remote transiently and never write it to config.

> Be precise with stakeholders: **environment variables are inherited by all descendant processes.** "It's only in the git process" is false the moment git spawns a child. The pipe/handle/stdin approach (A), served for the invocation lifetime and ACL-pinned, is what actually keeps it to the askpass reader; the clean-state invariant is what guarantees there are no *hostile* readers among git's children regardless.

### 5.4 Fallback: fine-grained PAT

A fine-grained PAT scoped to the repo + `Contents: Read and write` works as a coarser fallback: longer-lived (weeks/months → bigger blast radius), manual rotation, no per-operation narrowing. Fine for a single-repo prototype; graduate to the App for anything real. The same inheritance/clean-state rules apply.

---

## 6. The clean-state invariant — the `DoGit` design (centerpiece, v3-hardened)

**The rule:** the token-bearing git **never runs in the `.git` the agent wrote to.** It runs against an **orchestrator-owned `.git`** with **orchestrator-authored config**, in a **clean environment**, **with repo discovery pinned and bare-repo discovery refused**, and only the agent's **tracked tree content** crosses over from the agent's editing tree.

### 6.1 Two repos, one flow

- **Agent editing tree** (`$EDIT`): work tree + a `.git` the agent may freely read and *write* (config, `.gitattributes`, hooks, index) — and where it may *bury* arbitrary nested repos. **No token ever touches this.** The agent's mischief here is irrelevant because nothing here is ever the thing that pushes, *and* the transfer step (§6.2) imports only `$EDIT`'s tracked tree, not its raw file layout.
- **Clean push repo** (`$PUSH`): an **orchestrator-created** `.git` whose `config` the orchestrator wrote. Its config contains **only**:
  ```ini
  [core]
      # no fsmonitor, no sshCommand, no pager, no editor, no hooksPath needed
  [remote "origin"]
      url = https://github.com/owner/abox-server.git
      fetch = +refs/heads/*:refs/remotes/origin/*
  [user]
      name  = abox-orchestrator
      email = bot@abox.invalid
  ```
  It defines **no** `filter.*` (so no `filter.<x>.clean/.smudge/.process` *and* no `filter.<x>.required=true` — the latter is itself a config key the orchestrator does not set, so an in-tree `.gitattributes` **cannot** flip `required` on by itself), **no** `core.fsmonitor`, **no** `credential.helper`, **no** `diff.external`, **no** `[include]`/`includeIf`, **no** aliases. Therefore **any `.gitattributes` referencing a filter is inert** — *inert because `$PUSH`'s config defines no matching driver **and** sets no `filter.<x>.required=true`* — and there is no config-defined command for git to execute. That is the whole point: **clean config, not attribute scrubbing.**

  > **Note (clarified vs v2):** an in-tree `.gitattributes` cannot make a filter "required" on its own. `required` lives in *config* (`[filter "x"] required = true`), which `$PUSH` does not set. So a reader should not fear that committing `.gitattributes` flips a hard-fail/driver-call on.

Concretely, `$PUSH` can be: a **separate clone**, a **`git worktree add`** off an orchestrator-owned bare repo, or a separate **`GIT_DIR`** with its own `HEAD`/`index`/`config`. The cheapest robust option is an orchestrator-owned **bare clone** created once at flow start (the first token-bearing clone, §6.3), with a detached work area for staging.

### 6.2 Moving content across — tracked-tree transfer, NOT a name-filtered file mirror

> **Why "copy files excluding `.git` by name" is INSUFFICIENT (the v2 weakness).** On *both* target OSes — Windows and macOS are case-**insensitive** by default — `robocopy /XD .git` (or `rsync --exclude=.git`) does **not** exclude:
> - **`.GIT` / `.Git`** — different case, same directory on a case-insensitive FS;
> - **the NTFS 8.3 short name `GIT~1`** — resolves to a long name git treats as `.git`;
> - **a symlink whose target *resolves* to `.git`** — the CVE-2024-32002 class;
> - **a *buried bare repo*** — `tools/x/{HEAD,objects/,refs/,config}` is *not named* `.git` at all, so name exclusion never looks at it. Once mirrored into `$PUSH`, `git add -A` **discovers** it and reads its `config` (e.g. `[core] fsmonitor = <payload>`), running the payload **inside the process about to hold the token** (GHSA-9ccr-r5hg-74gf).
>
> A name-based exclusion is a *blocklist on path strings*, and like every blocklist it loses to case-folding, short names, symlinks, and "don't call it `.git` at all." So v3 does **not** transfer by filtering names.

For each `DoGit`, the orchestrator brings the agent's **tracked working-tree content** into `$PUSH` **without** importing the agent's `.git`, without importing a buried repo, and without letting a tracked `.gitattributes` resolve to a driver. **Use one of:**

**(a) Tree-object transfer (RECOMMENDED — cleanest, also fixes transfer fidelity).**
The orchestrator builds the commit from the agent's **tracked tree**, controlled entirely by git plumbing *the orchestrator runs*, never by mirroring the raw file layout. The agent's index/`.git` in `$EDIT` is *read* only to enumerate tracked content; a buried bare repo is **not tracked content of `$EDIT`** unless the agent `git add`-ed it, and even then it arrives as ordinary blobs under a path that the orchestrator writes into `$PUSH`'s index — it is never *discovered* as a repo, because we never run `add -A` over a raw mirror. Mechanically, the orchestrator:

1. derives the tree from `$EDIT`'s tracked working set (e.g. capture `$EDIT`'s tree via plumbing the orchestrator invokes, with the same pinned, bare-refusing flags as §6.4 applied to any read of `$EDIT`), then
2. materializes it into `$PUSH` with `git read-tree` / `git checkout-index` (no work-tree scan that could discover a buried repo), then commits and pushes from `$PUSH`.

Force the relevant platform-sensitive knobs **on the command line** (don't inherit platform defaults):
```
-c core.protectNTFS=true      # refuse paths that collide with NTFS 8.3 / device names / .git on NTFS
-c core.protectHFS=true       # refuse HFS+/APFS Unicode tricks that resolve to .git
-c core.symlinks=false        # do NOT recreate symlinks on checkout; materialize as inert (see Fix 5)
```
`core.protectNTFS`/`core.protectHFS` make git itself reject a path that would land as `.git` via 8.3 short name or Unicode case/normalization — exactly the smuggling Fix 2 targets — and they are forced on the command line so a hostile in-tree value can't turn them off.

**(b) Real-path-resolving, case-folded copy (acceptable alternative).**
If you must copy files rather than use plumbing, do **not** filter by string name. Walk `$EDIT`, and for each entry:
- **normalize and case-fold the final path component**; **reject** anything equal (case-folded, Unicode-normalized) to `.git`, and **reject** anything matching the 8.3 shortname pattern `GIT~%`;
- **resolve symlinks**: reject (or flatten to inert text) any symlink, and reject any path whose *real* path escapes the tree (path-traversal) or resolves into a `.git`;
- **reject any subtree that is itself a repository** — a directory containing the `HEAD` + `objects` + `refs` triple (a pre-commit scan), so a buried bare repo never lands.
Then stage in `$PUSH` with the pinned, bare-refusing flags of §6.4.

```bash
# After EITHER (a) or (b), the staging/commit/push runs in the CLEAN repo with the
# CLEAN, DISCOVERY-PINNED environment (see 6.4). Note the explicit dirs + bare refusal:
git -C $PUSH \
    -c safe.bareRepository=explicit -c core.protectNTFS=true -c core.protectHFS=true \
    --git-dir=$PUSH/.git --work-tree=$PUSH \
    add -A -- .
git -C $PUSH -c safe.bareRepository=explicit --git-dir=$PUSH/.git --work-tree=$PUSH commit -m "$MSG"
git -C $PUSH -c safe.bareRepository=explicit --git-dir=$PUSH/.git --work-tree=$PUSH push origin HEAD:$BRANCH
```

A committed `.gitattributes` *may* come across as a file — but it is **inert**, because `$PUSH`'s config defines no matching driver and sets no `filter.<x>.required=true`. We did not have to scrub it. (Belt-and-suspenders: you may additionally drop an orchestrator-authored `.git/info/attributes` of `* -filter -diff`; not required.)

> **REMOVED — v2 §6.2 "option 2" was UNSAFE.** v2 offered a second option:
> `git --work-tree=$EDIT --git-dir=$PUSH/.git add`.
> **DO NOT USE.** Pointing git's work-tree at `$EDIT` makes git **read `$EDIT`'s `.gitattributes` and discover `$EDIT`'s embedded/buried repos**, re-opening exactly Fix 1 and Fix 2 — the token-bearing process would scan the agent's tree and could run a buried `core.fsmonitor` or a tracked-attribute driver. The whole point of `$PUSH` is that the token-bearing git's work-tree is the orchestrator's, never the agent's. Removed.

> The orchestrator decides commit contents from the agent's **tracked tree**, never by mirroring `$EDIT`'s raw filesystem and never from the agent's index-as-authoritative. The agent's sneaky local commit / rewritten hook / staged secret / **buried bare repo** in `$EDIT` **cannot reach the remote and cannot execute** in the token-bearing process, because the remote is pushed only from `$PUSH`, which the orchestrator populated from the tracked tree under pinned discovery.

### 6.3 First clone & workspace lifecycle (who clones, when)

The very first `git clone` of the private repo **needs network + the token** — so it is itself a **token-bearing operation, performed while no agent is running.**

- **Created once per workspace (flow start):**
  1. Provision the persistent workspace directory and (Rung 1) the dedicated `abox-git` OS user.
  2. **Token-bearing op (no agent live):** the orchestrator mints a token and **clones** into `$PUSH` as an orchestrator-owned bare/clean repo, then **writes the clean `config`** (§6.1) — overwriting whatever the clone wrote, to be certain no `credential.helper`/`fsmonitor`/filter leaked in.
  3. Create `$EDIT` for the agent — a checkout/worktree of the same commit the agent will edit. (`$EDIT` may be a plain `git clone` from `$PUSH` locally, or a copy of the checked-out files; the agent owns it thereafter.)
  4. Generate the askpass helper + per-launch pipe machinery (served for the git-invocation lifetime, ACL-pinned — §5.3).
- **Per-flow / per-`DoGit`:** mint a fresh token; transfer the tracked tree `$EDIT → $PUSH` (§6.2); commit + push in `$PUSH` with clean, discovery-pinned env; dispose token in `finally`.
- **Teardown (§9 / walkthrough step 5):** delete `$EDIT` and `$PUSH` working areas; **delete the askpass temp file and close/destroy the named pipe**; ensure `ScopedToken.Dispose()` ran on every path (including the `DoGit` *failure* path — wrap the whole token lifetime in `try/finally`). Tokens expire within the hour regardless. What persists: the **audit log** and the **commits already on the remote**.

```csharp
// Fix 4: lock FIRST, then assert-no-agent UNDER the lock, then the whole token-bearing op.
await using var _ = await locks.AcquirePerUserWorkspaceAsync(user, workspaceId);  // serialize
AssertNoAgentLive(user, workspaceId);    // check-and-act atomic under the lock (no TOCTOU)

ScopedToken? token = null;
try
{
    token = await orchestrator.MintAsync(repo, perms: Contents.Write);   // just-in-time
    TransferTrackedTree(edit: $EDIT, push: $PUSH);                       // tree-object/safe copy, no .git, no buried repo
    await executor.RunGitCleanAsync($PUSH, gitArgs, token);              // token out-of-band arg, discovery pinned
}
finally
{
    token?.Dispose();              // hygiene; the real audit control is that it was never logged
    CleanupAskpassTemp();          // delete helper file, tear down the lifetime-scoped pipe
}   // lock released here
```

### 6.4 The clean environment for every token-bearing git (clean config + pinned discovery + bare refusal)

Run **every** token-bearing git invocation with global/system config neutralized, no inherited `GIT_*`, **and repo discovery pinned so git cannot wander into a buried bare repo**:

```csharp
var psi = new ProcessStartInfo("git") { UseShellExecute = false };
psi.EnvironmentVariables.Clear();                          // start from nothing; add only what we need

// Neutralize global/system config so no ~/.gitconfig or /etc/gitconfig is read.
// SUBTLETY (Fix 1): safe.bareRepository is PROTECTED config — git refuses to read it from
// repository-level config (it would mean trusting the very embedded repo we distrust). So it
// CANNOT live in $PUSH/.git/config. It must come from the command line (-c) OR from an
// orchestrator-authored GLOBAL config file. That COLLIDES with GIT_CONFIG_GLOBAL=NUL below:
//   • Option 1 (used here): keep GIT_CONFIG_GLOBAL=NUL, pass safe.bareRepository on EVERY
//     invocation as a -c flag (a -c value IS protected/trusted config).
//   • Option 2: point GIT_CONFIG_GLOBAL at an orchestrator-authored throwaway file that sets
//     ONLY [safe] bareRepository = explicit (and nothing else) — NOT NUL.
psi.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
psi.Environment["GIT_CONFIG_SYSTEM"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

// Defense in depth: stop discovery from walking UP or INTO a buried repo.
psi.Environment["GIT_DIR"]       = $"{push}/.git";        // pin the repo explicitly
psi.Environment["GIT_WORK_TREE"] = push;                  // pin the work-tree explicitly
psi.Environment["GIT_CEILING_DIRECTORIES"] = parentOf(push);  // never traverse above $PUSH

psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
psi.Environment["GIT_ASKPASS"] = askPassPath;             // §5.3 form A (pipe), not an inherited token
psi.Environment["ABOX_GIT_PIPE"] = pipeName;              // a name, not the secret
psi.Environment["PATH"] = minimalPath;                    // git + its needed helpers only
// Do NOT set ABOX_GIT_TOKEN here in form A; the token is served over the pipe.

// Per-invocation -c flags (protected/trusted config; cannot be overridden by in-tree state):
string[] hardening =
{
    "-c", "safe.bareRepository=explicit",   // REFUSE any bare repo not named via --git-dir/GIT_DIR (Fix 1)
    "-c", "core.protectNTFS=true",          // reject 8.3/.git-on-NTFS smuggling (Fix 2)
    "-c", "core.protectHFS=true",           // reject HFS/APFS unicode .git smuggling (Fix 2)
    "-c", "safe.directory=" + push,         // specific path, NOT the wildcard '*' (smaller correction)
    "--git-dir=" + push + "/.git",          // explicit, belt-and-suspenders with GIT_DIR
    "--work-tree=" + push,
};
psi.ArgumentList.Add("git");                 // (illustrative) followed by hardening, then the subcommand
```

On Windows use **`NUL`** (or an empty string) for the config paths, not `/dev/null`. The repo-local `$PUSH/.git/config` is the *only* config in play, and the orchestrator authored it.

**`safe.bareRepository=explicit` — the critical subtlety (Fix 1).** With `explicit`, git will operate on a bare repository **only** when it is named via `--git-dir`/`GIT_DIR`; it **refuses** to silently treat a *discovered* directory in the tree as a bare repo. So even if a buried `{HEAD,objects,refs,config}` survived into `$PUSH`, `git add -A` will **not** adopt it and **not** read its `config`/run its `core.fsmonitor`. Because `safe.bareRepository` is *protected* config, it **cannot** be set from `$PUSH/.git/config` — pass it as `-c` (used above) **or** via an orchestrator-authored `GIT_CONFIG_GLOBAL` file; on Windows, where `GIT_CONFIG_GLOBAL=NUL`, the `-c` form is the natural choice. **Optional pre-commit scan:** additionally reject any copied subtree containing a `HEAD`+`objects`+`refs` triple before staging.

**What about `safe.directory`?** Because we cleared global config, you may need `-c safe.directory=$PUSH` (the **specific path**, not the wildcard `*`) on the command line if ownership checks complain. Orchestrator-controlled, safe to set explicitly. Avoid `*`; it broadens the trust beyond the one repo.

### 6.5 Hooks are now a non-issue, but still belt-and-suspenders

Since `$PUSH` is orchestrator-created, it has **no agent-authored `.git/hooks`**. You may still pass `-c core.hooksPath=` and (`commit`/`push`) `--no-verify` as cheap insurance, but they are no longer load-bearing — **the load-bearing control is that the whole `.git` and its config are orchestrator-owned, that discovery is pinned, and that the tree is imported as tracked content under bare-repo refusal.** This is the key reframing from v1: we stopped relying on a per-key blocklist; v3 also stops relying on a per-*name* exclusion.

---

## 7. Operation Catalog

| Operation | Inputs | Outputs | Carries token? | Runs in which `.git`? | Notes |
|---|---|---|---|---|---|
| `RunAgent` | agent id (Claude/Codex/…), turn context, workspace | transcript, files mutated in `$EDIT` | **No** | `$EDIT` (agent-owned) | agent CLI as a child with a token-free env; may run read-only git. |
| `GetDiff` | workspace, optional base ref / pathspec | unified diff + diffstat | **No** | `$EDIT` (read-only) | `git diff` against the live tree. No token, no clean-state needed (read-only, no push). |
| `WriteFile` / `ApplyEdit` | path, content or patch | file written/patched in `$EDIT` | **No** | `$EDIT` | deterministic orchestrator-authored edits (format, version bump, generated file). |
| `RunBuild` | build command, workspace | exit code, log | **No** | n/a | `dotnet build ABox.slnx`. |
| `RunTests` | test command, workspace | exit code, results | **No** | n/a | `dotnet test ABox.slnx`. |
| `DoGit` | sub-action (`commit`/`push`), pathspec, message, **token (out-of-band)** | commit SHA / push result | **Yes** | **`$PUSH` (orchestrator-owned, clean, discovery-pinned)** | the only token-bearing op; agent NOT running; **tracked tree** imported from `$EDIT` (not a name-filtered mirror); clean env + bare refusal (§6.4); lock held across the whole op (§6.3, Fix 4). |
| `OpenPullRequest` | base, head, title, body, **token (`pull_requests:write`, out-of-band)** | PR number/url | **Yes (separate scope)** | n/a — REST API, not git | distinct token from `DoGit`; never enters a git child process. |

`GetDiff` deliberately runs in `$EDIT` and read-only: it never holds a token, so an attribute/config/buried-repo trigger there is harmless (worst case the agent's own machinery runs in a *token-free* process — exactly a `RunAgent` equivalent).

---

## 8. Windows Implementation (primary) & macOS (secondary)

### 8.1 Baseline (Rung 0) — distinct, explicit, non-inherited env per child

Baseline is two kinds of `Process.Start`, never concurrent. The shipping mechanism is `ProcessStartInfo { UseShellExecute = false }` with an **explicit, non-inherited environment** (clear it, add only what's needed):

```csharp
// Agent turn in $EDIT: NO token, agent owns its .git.
var agent = new ProcessStartInfo("claude", agentArgs) { UseShellExecute = false, WorkingDirectory = edit };
agent.EnvironmentVariables.Clear();
foreach (var kv in agentSafeEnv) agent.Environment[kv.Key] = kv.Value;   // no ABOX_GIT_* anything
// ...spawn, await exit...

// DoGit in $PUSH: token served over the lifetime-scoped, ACL-pinned pipe; clean + discovery-pinned env (§6.4).
// ...transfer tracked tree $EDIT -> $PUSH, then spawn git with the clean psi, await exit, finally dispose...
```

### 8.2 Rung 1 — run `DoGit` as a dedicated low-privilege user

Create `abox-git` once (non-admin, minimal rights). Run the clean-push git **as that user** so its env block is readable only by `abox-git` (+ admins). On Windows, the honest mechanism is **P/Invoke**, because `.NET`'s `ProcessStartInfo { UserName, Password }` **cannot supply a custom environment block cleanly**:

```text
LogonUser("abox-git", ".", password, LOGON32_LOGON_BATCH, PROVIDER_DEFAULT, out hToken);
CreateEnvironmentBlock(out env, hToken, inherit:false);   // base block for abox-git
// add our minimal vars (GIT_CONFIG_GLOBAL=NUL, GIT_DIR, GIT_WORK_TREE, GIT_CEILING_DIRECTORIES,
//                        GIT_ASKPASS, ABOX_GIT_PIPE) into 'env'
CreateProcessAsUser(hToken, gitExe, gitArgsWithHardeningFlags,
                    creationFlags: CREATE_UNICODE_ENVIRONMENT,
                    environment: env, ...);
```

Pin the askpass **named-pipe ACL to the `abox-git` SID** (§5.3) so only the user running `DoGit` can read the token off the pipe.

> **Honest caveat:** reading *another user's* process environment requires being that user **or** holding `SeDebugPrivilege`/admin. So Rung 1 stops a *non-admin* concurrent agent — **an admin agent defeats it.** If the agent could be admin, you need Rung 2 (confinement), not Rung 1.

A `gMSA` (group managed service account) is the cleanest variant — no password to hold; same custom-env-block path via the logon token.

**Quoting:** the askpass path may contain spaces (`C:\Program Files\…`). `GIT_ASKPASS` is invoked by git; ensure the path is correctly quoted/escaped for the platform so the helper actually launches. Prefer a space-free install path for the helper to sidestep this entirely.

### 8.3 Windows notes

- **Absolute-path git doesn't help the agent** — `git.exe` the agent runs has the agent's token-free env and runs in `$EDIT` (no token, no remote-with-credential). Nothing to find.
- **Config-driven execution** is handled by the clean-state invariant, not by enumerating keys; **discovery-driven execution** (buried bare repo, smuggled `.git`) is handled by `safe.bareRepository=explicit` + pinned `--git-dir`/`GIT_CEILING_DIRECTORIES` + `core.protectNTFS=true`, not by name exclusion.
- **Windows Sandbox / WSL2** is the Rung-2 option to confine the agent (no host process-table view).
- Use **`NUL`** / empty string for `GIT_CONFIG_GLOBAL`/`GIT_CONFIG_SYSTEM`, not `/dev/null`.

### 8.4 macOS (secondary)

- **Baseline:** `Process` with a per-child explicit environment; clean-state invariant identical; `GIT_CONFIG_GLOBAL=/dev/null`, `GIT_CONFIG_SYSTEM=/dev/null`; same `-c safe.bareRepository=explicit -c core.protectHFS=true` + pinned `GIT_DIR`/`GIT_WORK_TREE`/`GIT_CEILING_DIRECTORIES`. **macOS (APFS/HFS+) is case-insensitive by default — so name-based `.git` exclusion is just as insufficient here as on Windows; use the tracked-tree transfer.**
- **Rung 1:** service account `_aboxgit`; run the clean-push git via `launchd`/`sudo -u _aboxgit` with the token served over a `0600` socket/FIFO **owned by `_aboxgit`** in a `0700` temp dir. `ps eww` shows env only for your own processes / as root — same property as Windows §8.2, same admin caveat.
- **Rung 2 (confine agent):** a Linux container under the macOS container runtime, or a restricted user. **Caveat:** Apple's `sandbox-exec`/Seatbelt is **deprecated** (still present, but unsupported and warned-against) — do not build new confinement on it; prefer a container/VM. Note it, don't depend on it.

---

## 9. Failure Modes, Residual Risks, Honesty

**What this design provides (at the §1 bar):**

- An **improvising agent cannot push**: during its turn no token exists in any process it can read (I1), and `DoGit` runs only when it doesn't.
- **The agent's `.git` config/attributes/index cannot execute code in the token-bearing process** (I2): that process runs in `$PUSH`, an orchestrator-owned clean `.git`, with global/system config neutralized; an in-tree `.gitattributes` is inert because no matching driver is defined *and* no `filter.<x>.required=true` is set. This closes the `core.fsmonitor`/`filter.*`/`core.sshCommand`/`core.pager`/`diff.external`/`credential.helper`/`include`/`gpg.program`/alias class **at once**, including future variants — without a blocklist.
- **A repository the agent buried in its tree cannot execute code in the token-bearing process** (I2, v3): the transfer step imports the agent's *tracked tree* (not a name-filtered mirror), and the token-bearing git runs with `safe.bareRepository=explicit` + pinned `--git-dir`/`GIT_CEILING_DIRECTORIES` + `core.protectNTFS/protectHFS=true`, so neither a buried bare repo nor a case-folded/short-name/symlinked `.git` survives transfer or gets *discovered* and run. This closes the GHSA-9ccr-r5hg-74gf and CVE-2024-32002 classes.
- **The agent's local commit/hook/staged-secret mischief cannot reach the remote** — the orchestrator pushes from `$PUSH`, populated from the tracked tree.
- **The token is short-TTL, narrowly scoped, re-minted per push** (≤1h, this repo, `contents:write`), and **not inherited by git's children** when served over the lifetime-scoped, ACL-pinned pipe (§5.3-A).
- **The audit log never contains the token** — because the token is **not a property of any loggable/dispatched type** (out-of-band argument), verified by a log-sink round-trip test (§4.2), not merely by `[JsonIgnore]`.

We deliberately **do not** claim "structurally impossible." The accurate claim: *the token-bearing repo contains zero agent-authored config, attributes, index, or discoverable buried repository, so there is no agent-controlled execution path in the token-bearing process — given the clean-state invariant and the tracked-tree transfer under pinned discovery, backstopped by branch protection.*

**What it does NOT defend against (be explicit):**

- **Persistent background watcher / process-memory spy** (§1 cases 1–2). A determined implant inside the agent that waits for `DoGit` and reads that sibling's env/memory defeats the *baseline*. Mitigation: the ladder — Rung 1 (separate user; **admin agent defeats it**) and Rung 2 (confine the agent). At baseline, accept this risk knowingly.
- **Code exfiltration to an agent-controlled remote.** With open egress the agent can copy the *code* out. That's "leak my code," different from "write to my repo," **out of scope unless egress control (Rung 3) is added.**
- **A buggy executor.** The executor must (a) never put the token in the agent's env, (b) never overlap the two processes, (c) **never run the token-bearing git in the agent's `.git` or with agent-readable global/system config**, and now (d) **never import the agent's tree as a name-filtered mirror, and always run the token-bearing git with `safe.bareRepository=explicit` + pinned discovery so a buried/smuggled repo can neither survive transfer nor be discovered.** This correctness is small and auditable — and it *is* the trust anchor. Test all four directly.
- **Compromise of the orchestrator host** (where the master private key lives) loses everything. Protect that key (DPAPI/Keychain/secret store; ideally a managed identity so the raw PEM isn't on disk).

**The backstop that holds even if every local control fails — and its real scope.** v1 said "required reviews." That is **not enough**: a `contents:write` installation token can **create branches and force-push** unless protection forbids it. The backstop must be **branch protection / a ruleset on the protected branch that includes all of:**

- **require a pull request + required reviews** before merge;
- **restrict who can push** to the protected branch (and prefer PR-only — no direct pushes to `main`);
- **block force-pushes** (`allow_force_pushes = false`);
- **block deletions**;
- **do NOT exempt the App / bots from the ruleset** — if "allow specified actors to bypass" includes the App, the backstop is void. Enforce on admins too.

**Programmatic assertion (not just a "check").** At flow start the orchestrator must **assert** these ruleset properties via the API and **refuse to start** if any is missing — specifically, it **fetches the ruleset's bypass-actors list and asserts the App's actor id is NOT in it** (treat presence as a hard failure, not a warning), alongside required-PR/reviews, restrict-push, block-force-push, block-deletions. This is an assertion in code at `FlowStart`, equivalent to a test the flow runs against the live ruleset — not a manual checklist.

With those, even if an agent somehow obtained a token, it can push only to a **non-protected branch** that **cannot merge to `main`** without human review, and **cannot force-push or delete** the protected branch. This is the last line; it does not depend on any local guarantee being intact. **Treat it as mandatory.**

Reconciling scope one more time: the **push token** is `contents:write` and **cannot open a PR**; **opening a PR** is a *separate* operation with a *separate* `pull_requests:write` token that **never enters a git child process** (§5.1, §7).

---

## 10. Build Notes (this repo)

- Token mint, the clean-push repo setup, the tracked-tree transfer, and the `Operation` model fit as `Steps`/services in the `ABox` orchestrator; process-spawn + the clean-env / discovery-pinning construction is `Core` infra. Keep `ScopedToken` and the **out-of-band passing** discipline in the dispatch types so neither the JSON store nor the Serilog sink can see a token (PRD spine: validators-are-Steps, no hidden statics — the token is a constructed, disposed collaborator).
- **Testable invariants** (assert in Unit / E2E / Wire rulebooks, don't trust review):
  - the agent's env contains **no** `ABOX_GIT_*` / token (Unit);
  - **no** concurrent agent process during `DoGit`, **and** the no-agent-live assertion happens *while holding the per-(user,workspace) lock* (E2E — the TOCTOU regression, Fix 4);
  - the token-bearing git runs with `GIT_CONFIG_GLOBAL`/`GIT_CONFIG_SYSTEM` neutralized, **against `$PUSH` not `$EDIT`**, **and with `-c safe.bareRepository=explicit` + pinned `--git-dir`/`GIT_CEILING_DIRECTORIES` + `core.protectNTFS/protectHFS=true`** present on the command line (Unit/E2E);
  - a committed `.gitattributes`/`core.fsmonitor` in `$EDIT` does **not** cause any extra process spawn during `DoGit` (E2E — regression for the v1 config-driver flaw);
  - **a *buried bare repo* (`tools/x/{HEAD,objects,refs,config}` with `[core] fsmonitor=<payload>`) committed in `$EDIT` causes NO extra process spawn during `DoGit`** (E2E — Fix 1 regression, GHSA-9ccr-r5hg-74gf class); assert process-spawn count is unchanged vs a clean tree;
  - **a `.GIT` / `GIT~1` / symlinked-`.git` path in `$EDIT` does NOT survive transfer into `$PUSH`** (E2E — Fix 2 regression; assert no `$PUSH/**/.git`-equivalent lands, and that `core.protectNTFS/protectHFS` reject it);
  - the askpass channel **answers multiple askpass calls within one `git push` and is torn down in `finally`**, and its ACL admits only the `abox-git` SID / `0600` owner (Unit — Fix 3);
  - an `OperationRequest`/`OperationRecord` round-tripped through the **real Serilog sink and the real JSON serializer** yields **no** token substring (Wire);
  - at `FlowStart`, the ruleset assertion **fails the flow** when the App is in the bypass-actors list or any required protection is absent (E2E/Unit — §9 programmatic backstop).
- Provider-agnostic by construction: `RunAgent` only knows "spawn this CLI with this token-free env"; Claude Code vs Codex vs any CLI is just the command.

---

## 11. End-to-End Walkthrough: One Flow, Step by Step

A concrete, ordered narration an engineer can implement from. Flow:
**RunAgent(A: implement) → GetDiff → RunAgent(B: review) → DoGit(commit+push) → RunAgent(A: address feedback) → DoGit → orchestrator WriteFile → DoGit.**
One persistent workspace throughout. `agent` = Claude Code / Codex / any CLI. `owner/abox-server` is the private repo.

### Step 1 — Deciding to run a flow

1. **Trigger.** Something kicks the orchestrator off: a webhook (issue/PR comment, label), a schedule, a CLI/API call, or an operator pressing "go." The trigger names a **flow definition** and a **target repo**.
2. **Load.** The orchestrator loads:
   - the **flow definition** — the ordered list of operations (the turn script above);
   - the **repo identity** — `owner/abox-server`, default branch, the **work branch** name (e.g. `abox/feature-x`, never `main`);
   - the **GitHub App installation id** for that repo (the App must be installed on it);
   - the **master private key** handle (from DPAPI/Keychain/secret store — not loaded into a logger-reachable field).
3. **Validate up front (fail fast, before any agent runs):**
   - the App is installed on `owner/abox-server` and can mint a token with the scopes the flow needs (do a dry **mint-and-discard** to confirm, or check installation permissions);
   - **branch protection / ruleset on the default branch is present and correct, asserted programmatically** (§9: required PR + reviews, restrict push, block force-push/delete). **Fetch the ruleset's bypass-actors list and ASSERT the App's actor id is NOT present** — treat its presence as a hard failure. *If the backstop isn't in place (or the App can bypass it), refuse to start* — the backstop is mandatory.
   - the workspace root is writable; the `abox-git` user exists (Rung 1); the askpass helper path is space-free and present.
   - Log an `OperationRecord{Kind=FlowStart}`; **no token involved.**

### Step 2 — Standing up what's needed

**Created once per workspace** (not per turn):

1. **Persistent workspace dir** `W/` on disk, alive for the whole flow.
2. **(Rung 1) the `abox-git` OS user** — created once at install time, reused across flows (non-admin, minimal rights; or a gMSA on Windows).
3. **The askpass helper + lifetime-scoped, ACL-pinned pipe machinery** — a small program at a space-free path, plus the executor's ability to create a per-launch named pipe (`\\.\pipe\abox-git-<guid>` on Windows with an ACL admitting only the `abox-git` SID, a `0600` FIFO/socket owned by `_aboxgit` in a `0700` temp dir on macOS) that serves the token to **every askpass call of the one git invocation**, then is torn down.

**Created per flow** (next step does the network part):

4. **`$PUSH`** — the orchestrator-owned clean repo (built in Step 3, since it needs the token).
5. **`$EDIT`** — the agent's editing tree (built in Step 3 from the cloned contents).

Nothing here mints or holds a token yet.

### Step 3 — Setup operations (the FIRST clone — token-bearing, no agent running)

This is the **first token-bearing op**, run while **no agent is live** (under the per-(user,workspace) lock — Fix 4):

1. **Mint** a short-lived installation token scoped `{ repositories: [abox-server], permissions: { contents: "write" } }` (§5.2). Hold it as an out-of-band `ScopedToken`.
2. **Clone into `$PUSH`** with the **clean, discovery-pinned environment** (§6.4) and the token served over the pipe (§5.3-A):
   ```bash
   git -c safe.directory=$PUSH \           # SPECIFIC path, not the wildcard '*'
       -c safe.bareRepository=explicit \   # refuse discovered bare repos (Fix 1)
       -c core.protectNTFS=true -c core.protectHFS=true \
       -c credential.helper= \
       clone --branch <default> https://github.com/owner/abox-server.git $PUSH
   #   env: GIT_CONFIG_GLOBAL=NUL/-dev-null, GIT_CONFIG_SYSTEM=NUL/-dev-null,
   #        GIT_CEILING_DIRECTORIES=<parent of W>, GIT_TERMINAL_PROMPT=0,
   #        GIT_ASKPASS=askpass, ABOX_GIT_PIPE=\\.\pipe\abox-git-<guid>
   ```
3. **Overwrite `$PUSH/.git/config`** with the orchestrator-authored clean config (§6.1): remote URL + identity only. Strip anything the clone added (e.g. a `credential.helper`). This is the establishing act of the clean-state invariant — from here on, `$PUSH`'s config is known-clean.
4. **Create `$EDIT`** — check out the same commit into the agent's editing tree (a local clone/worktree/file-copy of `$PUSH`'s checkout). The agent owns `$EDIT` and its `.git` from now on.
5. **Dispose the token** in `finally`; tear nothing else down. **Log** `OperationRecord{Kind=Setup, CommitSha=<base>}`. **Token NOT logged.**

State now: `$PUSH` (clean, orchestrator-owned, no token resident) + `$EDIT` (agent-owned, never sees a token).

### Step 4 — The turns

For each turn: **who** is spawned, **as which user**, **with which env**, **token present?**, and the **exact `OperationRecord`** logged.

---

**Turn 1 — `RunAgent(A, "implement feature X")`**
- **Spawn:** `agent` (Claude Code / Codex) as a child, **as the normal user**, `WorkingDirectory=$EDIT`.
- **Env:** explicit, cleared, agent-safe; **no `ABOX_GIT_*`, no token.**
- **Token present?** **No.**
- Agent edits files in `$EDIT`, may run read-only git there; its `.git` mischief — and any repo it buries in the tree — is irrelevant.
- **Logged:** `OperationRecord{Kind=RunAgent, Inputs={agent:A, prompt:…}, ResultSummary:"N files changed", Status:Succeeded}`. No token (none existed).

**Turn 2 — `GetDiff`**
- **Spawn:** `git -C $EDIT diff` (read-only), normal user, token-free env.
- **Token present?** **No.** (Read-only in `$EDIT`; even if a hostile attribute or buried repo fired, the process holds no token — harmless.)
- **Logged:** `OperationRecord{Kind=GetDiff, ResultSummary:"<diffstat>"}`.

**Turn 3 — `RunAgent(B, "review this diff", diff)`**
- **Spawn:** `agent` as reviewer, normal user, token-free env, `$EDIT` (or a read-only view).
- **Token present?** **No.**
- **Logged:** `OperationRecord{Kind=RunAgent, Inputs={agent:B}, ResultSummary:"<verdict>"}`.

**Turn 4 — `DoGit(commit "feat: X", push)` — the first push**
Concrete clean-state mechanics (lock-across-check, tracked-tree transfer, bare refusal):
1. **Acquire the per-`(user, workspace)` lock**, and **only under that lock assert no agent process is live** (Fix 4 — check-and-act is atomic; the lock is held across the entire op below and released in `finally`).
2. **Mint** a fresh `{ contents: "write" }` token (out-of-band `ScopedToken`).
3. **Transfer the agent's TRACKED TREE `$EDIT → $PUSH` — NOT a name-filtered mirror** (§6.2, Fix 1+2+5):
   - preferred: build the commit content from `$EDIT`'s tracked working set via orchestrator-run plumbing and materialize it into `$PUSH` with `read-tree`/`checkout-index`, forcing `-c core.protectNTFS=true -c core.protectHFS=true -c core.symlinks=false` on the command line;
   - this is the agent's **tracked working-tree state** (Fix 5): it preserves an intentional force-add of a `.gitignore`-ignored file (it's tracked), and the orchestrator decides mode-bit/symlink handling (symlinks materialized as inert, not recreated) rather than depending on robocopy fidelity.
   - A committed `.gitattributes` rides along as a *file* — **inert**, because `$PUSH`'s config defines no filter driver and sets no `filter.<x>.required=true`. A **buried bare repo** or a **`.GIT`/`GIT~1`/symlinked-`.git`** does **not** survive: it is not tracked content materialized as a `.git`, and `core.protectNTFS/protectHFS` would reject any path resolving to `.git`.
4. **Stage + commit + push in `$PUSH` with the clean, DISCOVERY-PINNED env** (§6.4), token served over the lifetime-scoped, ACL-pinned pipe (§5.3-A), as the **`abox-git` user** (Rung 1) or the normal user (Rung 0):
   ```bash
   git -C $PUSH -c safe.bareRepository=explicit -c core.protectNTFS=true -c core.protectHFS=true \
       --git-dir=$PUSH/.git --work-tree=$PUSH add -A -- .
   git -C $PUSH -c safe.bareRepository=explicit --git-dir=$PUSH/.git --work-tree=$PUSH \
       -c safe.directory=$PUSH commit -m "feat: X"
   git -C $PUSH -c safe.bareRepository=explicit --git-dir=$PUSH/.git --work-tree=$PUSH \
       push origin HEAD:abox/feature-x
   #  env: GIT_CONFIG_GLOBAL=NUL, GIT_CONFIG_SYSTEM=NUL, GIT_DIR=$PUSH/.git, GIT_WORK_TREE=$PUSH,
   #       GIT_CEILING_DIRECTORIES=<parent of W>, GIT_TERMINAL_PROMPT=0,
   #       GIT_ASKPASS=askpass, ABOX_GIT_PIPE=<pipe>  — NO inheritable token var
   ```
   `safe.bareRepository=explicit` ensures `add -A` will **not** adopt or read the `config` of any directory in the tree that looks like a bare repo. git spawns `git-remote-https` + the askpass helper; the askpass helper is called **more than once** (username, then password, and again on any retry/redirect) and each call is answered off the **lifetime-scoped** pipe; **no child inherits the token from env.** No hostile config-defined child and no discovered buried repo exists (clean config + bare refusal).
5. **Dispose token in `finally`**; the pipe is torn down. **Release the lock in `finally`.**
6. **Logged:** `OperationRecord{Kind=DoGit, Inputs={subaction:"commit+push", message:"feat: X", branch:"abox/feature-x"}, CommitSha:<sha>, Status:Succeeded}`. **NOT logged:** the token (it was never a property of the record/request; the log-sink round-trip test proves it).

**Turn 5 — `RunAgent(A, "address B's feedback", feedback)`**
- Same as Turn 1: `agent` A in `$EDIT`, normal user, **no token.** Logged as `RunAgent`.

**Turn 6 — `GetDiff`** — same as Turn 2, **no token.**

**Turn 7 — `DoGit(commit "fix: review feedback", push)`** — same mechanics as Turn 4: acquire lock → assert no agent live under the lock → mint fresh token → transfer `$EDIT`'s tracked tree into `$PUSH` (no `.git`, no buried repo, protectNTFS/HFS forced) → clean-env, bare-refusing commit+push from `$PUSH` → dispose + release in `finally`. Fresh token, fresh pipe. Logged `DoGit{message:"fix: review feedback", CommitSha:<sha2>}`; token not logged.

**Turn 8 — `WriteFile(version.txt, "1.2.0")` (orchestrator, deterministic)**
- **No process spawned for the edit** (orchestrator writes the file into `$EDIT` directly), no agent, **no token.**
- **Logged:** `OperationRecord{Kind=WriteFile, Inputs={path:"version.txt"}}`.

**Turn 9 — `DoGit(commit "chore: bump version", push)`** — same clean-state mechanics; the deterministic file rides over with everything else as part of `$EDIT`'s tracked tree. Logged `DoGit{message:"chore: bump version", CommitSha:<sha3>}`; token not logged.

*(Optional Turn 10 — `OpenPullRequest`)* if the flow opens the PR itself: a **separate** token minted `{ pull_requests: "write" }`, used against the **REST API only** — it **never enters a git child process**. Logged `OperationRecord{Kind=OpenPullRequest, ResultSummary:"PR #N"}`; token not logged.

Token lifecycle, every `DoGit` (4, 7, 9): **lock → assert-no-agent under lock → mint just-in-time → serve over the lifetime-scoped ACL-pinned pipe to git's (multiple) askpass calls → push under bare-refusal + pinned discovery → dispose + release in `finally`.** Three independent short-lived tokens, never one cached. At no point in turns 1–3, 5–6, 8 does a token exist in any process the agent can reach; at every `DoGit` the token-bearing git runs in `$PUSH` against orchestrator-authored state, importing only the agent's tracked tree, with no discoverable buried repo.

### Step 5 — Teardown

1. **Tokens** — already disposed per op; all expire within the hour regardless. Nothing to revoke (but you *may* call the revoke endpoint for immediacy).
2. **Ephemera deleted:** `$EDIT` and `$PUSH` working areas; the **askpass temp file**; the named pipe / FIFO is closed and removed; any staging scratch. Verify `ScopedToken.Dispose()` ran on every path, including failed `DoGit`s (`try/finally`).
3. **`abox-git` user / gMSA / installed askpass helper** — **persist** (reused by the next flow), not torn down per flow.
4. **What persists deliberately:** the **audit log** (every `OperationRecord`, no token anywhere in it) and the **commits/branch already pushed to the remote** (`abox/feature-x` with `<sha>`, `<sha2>`, `<sha3>`), awaiting human review before merge to `main`.
5. **Concurrency / serialization scope.** A `DoGit` for flow X must **not** run while flow Y's agent is live **on the same user/host/workspace** (else a same-user concurrent agent could introspect the token-bearing git's env). Serialize token-bearing ops **per host/user/workspace**: hold a per-`(user, workspace)` lock around each `DoGit` **and assert no-agent-live while holding it** (Fix 4 — atomic check-and-act), never overlapping *any* agent of *any* flow on that user with *any* token-bearing git on that user. Independent users/hosts may run in parallel.

---

### Sources

- [Authenticating as a GitHub App installation — GitHub Docs](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/authenticating-as-a-github-app-installation)
- [Permissions required for GitHub Apps — GitHub Docs](https://docs.github.com/en/rest/authentication/permissions-required-for-github-apps) (push = `contents:write`; open PR = `pull_requests:write`)
- [REST: create an installation access token (`repositories` + `permissions` narrowing) — GitHub Docs](https://docs.github.com/en/rest/apps/apps#create-an-installation-access-token-for-an-app)
- [Generating a JSON Web Token (JWT) for a GitHub App — GitHub Docs](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-json-web-token-jwt-for-a-github-app)
- [About protected branches / rulesets (require PR, restrict push, block force-push, no bypass) — GitHub Docs](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches)
- [git config: introduce `safe.bareRepository` and protected config (protected config is NOT read from repository-level config) — git mailing list / GitGitGadget](https://public-inbox.org/git/pull.1261.v8.git.git.1657834081.gitgitgadget@gmail.com/)
- [justinsteven — Git buried bare repos and `core.fsmonitor` abuses (2022; config-defined drivers as ACE primitive; in-tree attributes inert without a config-defined driver)](https://github.com/justinsteven/advisories/blob/main/2022_git_buried_bare_repos_and_fsmonitor_various_abuses.md)
- [GHSA-9ccr-r5hg-74gf — "Nested Bare Repository via `core.fsmonitor`" (the verified advisory for the buried-bare-repo code-execution class; reported as CVE-2026-45033 — the bare CVE id could not be independently verified, so cite the GHSA)](https://github.com/advisories)
- [CVE-2024-32002 — recursive clone on case-insensitive filesystems with symlink support writes into `.git` and executes hooks (the case-fold / symlinked-`.git` smuggling class; mitigated by `core.protectNTFS`/`core.protectHFS`)](https://github.com/git/git/security/advisories/GHSA-8h77-4q3w-gfgv)
- [git-config / gitattributes — `filter.*.clean/.smudge/.process` + `filter.*.required`, `core.fsmonitor`, `core.sshCommand`, `diff.external`, `credential.helper`, `core.protectNTFS`, `core.protectHFS`, `core.symlinks`, `safe.bareRepository`, `safe.directory`, `include`/`includeIf` (config-driven external commands; protected config)](https://git-scm.com/docs/git-config)
- [gitcredentials — `GIT_ASKPASS` is invoked once per credential field (username, then password) and again on retry/redirect, so it is called more than once per push](https://git-scm.com/docs/gitcredentials)
