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

   Restart the Claude session after editing so the new `env` is picked up.

**Trade-off (Practical tier):** the agent runs as your Windows user, so it *could*
read your keyring; this setup makes the bot its only wired-up path and won't act as
you by accident. The ruleset below is the real backstop. For an airtight guarantee,
run the agent under a separate Windows user or a sandbox.

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
