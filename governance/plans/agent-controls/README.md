# Agent / owner identity — replicable setup

Goal: **the agent authors, only the owner lands.** Every Claude Code session acts
as the machine account `ABox-Agent`; you (MgCohen) keep working manually as
yourself; the agent provably cannot land changes to protected paths without your
review. This is the "Practical" tier (see `RETURN-PLAN.md`): the hard guarantee is
the GitHub ruleset with an empty bypass list, not OS-level credential isolation.

Two halves: a **one-time machine setup** (identity) and a **per-repo onboarding**
step (enforcement).

## 1. One-time machine setup (identity)

Done once per machine; applies to **every** project automatically.

1. **Create the bot token.** Sign in to GitHub as `ABox-Agent` →
   Settings → Developer settings → **Personal access tokens (classic)** → generate
   with scopes `repo` + `workflow`, short expiry. *(Classic, not fine-grained:
   fine-grained PATs can't reach repos owned by another personal account where the
   bot is only a collaborator.)*

2. **Put it in the global Claude settings `env` block** (`~/.claude/settings.json`).
   Claude injects these into its own subprocesses only — your interactive shells
   and SourceTree never see them, so they stay authenticated as you.

   ```json
   "env": {
     "GH_TOKEN": "<ABox-Agent classic PAT>",
     "GIT_CONFIG_COUNT": "2",
     "GIT_CONFIG_KEY_0": "credential.https://github.com.helper",
     "GIT_CONFIG_VALUE_0": "",
     "GIT_CONFIG_KEY_1": "credential.https://github.com.helper",
     "GIT_CONFIG_VALUE_1": "!gh auth git-credential",
     "GIT_AUTHOR_NAME": "ABox-Agent",
     "GIT_AUTHOR_EMAIL": "294015314+ABox-Agent@users.noreply.github.com",
     "GIT_COMMITTER_NAME": "ABox-Agent",
     "GIT_COMMITTER_EMAIL": "294015314+ABox-Agent@users.noreply.github.com"
   }
   ```

   - `GH_TOKEN` → `gh` and the `gh auth git-credential` helper both authenticate as
     the bot, with no global `gh auth switch`.
   - The two `GIT_CONFIG_*` helper entries reset any inherited credential helper
     (your Git Credential Manager) and route github.com auth through `gh` — but
     only inside Claude's processes, because env-injected git config is per-process.
   - The `GIT_*_NAME/EMAIL` make the commit author cosmetically the bot too.
   - **Caution — deliver this only via `env`.** Never bake the github.com helper
     override into the global `~/.gitconfig` (`git config --global ...`): it applies
     to *every* git on your machine, including SourceTree, routing your manual auth
     through the bot's `gh` (which is logged out) and breaking your push/pull. The
     `env` channel is per-process on purpose — keep it there.

   Restart the Claude session after editing so the new `env` is picked up.

3. **Harden: remove the owner's creds from the agent's default toolpath.** Without
   this, an agent that simply clears `GH_TOKEN` falls back to *your* stored
   credentials and can act as you (and thus self-approve). Close that fallback:

   - Log the owner out of the `gh` CLI: `gh auth logout --hostname github.com --user MgCohen`.
   - Sweep any Git Credential Manager github entry:
     `"protocol=https`nhost=github.com`n" | git-credential-manager erase`.
   - **SourceTree needs a one-time re-auth — it is *not* untouched.** SourceTree's
     git push/pull transport uses exactly the GCM `git:https://github.com` entry you
     just erased. (`sourcetree-rest:*` is only SourceTree's REST integration, not git
     auth — don't confuse the two.) After the erase, the next fetch prompts once; sign
     in as the owner via Git Credential Manager and it re-stores the credential.
     Verify the entries with `cmdkey /list | Select-String github`.

   After this, the **`gh` fallback is dead**: an agent with `GH_TOKEN` unset gets
   *empty* from both `gh` and `gh auth git-credential`. This kills the `gh` path —
   not every path. The owner's `git:https://github.com` credential still lives in
   Windows Credential Manager (SourceTree needs it); see the trade-off below for what
   that leaves reachable.

**Trade-off (Practical tier):** the agent runs as your Windows user, so it shares one
credential store with SourceTree. Step 3 kills the `gh` fallback, but the owner's
`git:https://github.com` credential must stay in Windows Credential Manager for
SourceTree to work. Because the bot routing lives only in Claude's per-process `env`
(never the global `.gitconfig` — see the caution in Step 2), an agent that
*deliberately* clears its env and runs plain `git` falls back to `manager` →
Credential Manager → the owner credential. This never happens by accident or in a
low-context session — the default path is always the bot — but it *is* reachable, via
plain git, not just the `CredRead` API. This is the strongest *same-user* posture; the
ruleset below is the real backstop against the bot (though not against stolen owner
creds — those act as a code owner). For a *truly* impossible guarantee, run the agent
under a separate Windows user or a WSL/container sandbox — which is also the
provider-agnostic seam (see "Multi-provider" below).

## 2. Per-repo onboarding (enforcement)

For each new repo, run as **yourself** (not the bot):

```powershell
./new-project-bootstrap.ps1 -Repo MgCohen/<new-repo> `
  -StatusChecks 'build-test (ubuntu-latest)','build-test (windows-latest)'
```

It adds `ABox-Agent` as a write collaborator and applies the `protect-main`
ruleset (require PR + 1 approval + code-owner review + dismiss-stale +
last-push-approval, block deletion / force-push, **empty bypass list**). Omit
`-StatusChecks` if the repo has no required CI yet.

Then commit a `CODEOWNERS` so protected paths request your review (this repo
generates it from `governance/protected-paths`).

## Why this holds

GitHub keys "can't approve your own pull request" on the **account that opens the
PR**. The agent opens PRs as `ABox-Agent`; approval must come from a *different*
account with code-owner rights — you. With the bypass list empty, not even the
owner can merge a protected-path change without that review. Convention becomes a
hard rule.

## Multi-provider note

The **enforcement** half (ruleset + CODEOWNERS, server-side on GitHub) is already
provider-agnostic — it keys on whatever credential is presented at push/PR time,
not on which tool pushed. The **credential-delivery** half currently rides Claude's
`~/.claude/settings.json` `env`, so it only fires for Claude Code's subprocesses.

That's deliberate YAGNI: today the only dev harness is Claude Code. The product's
*runtime* claude/codex agents do end-user work and don't push to this repo, so they
need no bot identity — don't wire it into the Flows/Agents layer.

When a second *dev* harness (e.g. Codex CLI) actually enters the loop, lift
credential delivery one layer down so every provider inherits it from one place,
instead of duplicating the `env` block per harness:

- **Workspace-bound** (light): a credential helper in the bot's working clone
  (`.git/config`, local & uncommitted) — anything running git there is the bot.
- **OS-principal-bound** (airtight): the separate Windows user / WSL / container —
  whatever runs as that principal is the bot, and it also delivers the "impossible
  to use the owner's creds" guarantee.

Nothing on the GitHub side changes either way.
