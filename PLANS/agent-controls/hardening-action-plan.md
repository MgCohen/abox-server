# Agent/owner git controls — hardening action plan

Action items from the adversarial audit of the agent/owner identity-separation +
repo-controls + critical-path-alert system (`MgCohen/abox-server`). Each item:
**what / why / change / expectation**. Severity: C(ritical) > H(igh) > M(edium) > L(ow).

**Deferred — not in this plan (deliberate):**
- **C1 — same-user credential isolation.** The agent runs as the owner's Windows
  user and can `CredRead` the owner's tokens. Real fix is OS-level isolation
  (separate user / VM / container). Tracked for the VM tier; out of scope now.
- **C3 — bot PAT rotation + move out of plaintext.** Not actioned now by owner's
  call. (Token remains plaintext in global `~/.claude/settings.json`.)

## Actions

| ID · Sev | Done | What | Why | Change | Expectation |
|---|---|---|---|---|---|
| C2 · H | ☐ | Keep sensitive secrets out of PR-triggered CI | Bot can author `ci.yml` on a PR branch; same-repo PRs run with secrets *before* merge → exfil | Document the constraint: only low-value `NTFY_TOPIC` in PR-triggered steps, never a sensitive secret; owner verifies Actions → PR-run approval setting. *Deferred note (don't build now):* when a sensitive channel is first added, gate it behind a protected **Environment** (required reviewer) or a post-merge `push` job | No meaningful secret reachable by a bot-authored PR workflow; mechanism added only on the second real use |
| H1 · H | ☐ | Make the `.claude` PreToolUse guard real, or drop the claims | It's git-ignored/absent, yet ADR0010 + README + policy call it a live enforcer → CODEOWNERS row inert, "can't disable itself" is false | Inspect repo `.claude/settings.json` + `.claude/hooks/` (confirm no secrets) → un-ignore + commit; **or** strike it from ADR0010/README/`protected-paths` | 4th enforcer is real + owner-gated, or docs stop claiming a phantom |
| H2 · H | ☐ | Protect the arch/structure enforcer *code* | Rulebook markdown is protected but `tests/Tests/Arch/**` & `Structure/**` (the asserting tests) are freely editable → agent can weaken a rule to pass | Add `tests/Tests/Arch/** \| @MgCohen \| critical` and `tests/Tests/Structure/** \| @MgCohen \| critical`; regenerate CODEOWNERS | Weakening a structural assertion needs owner review + alert |
| M1 · M | ☐ | Resync CODEOWNERS | Committed file drifted from generator (row order + CRLF) → "always regenerate" invariant already broken | Run `generate-codeowners.sh` on an LF checkout, commit; add a CI check asserting generator output == committed | CODEOWNERS == generator output; drift fails CI |
| M2 · M | ☐ | Add `.gitattributes` (LF) | Enforcer `sh` scripts are CRLF, run on Linux runners — survive by luck; one refactor breaks a guard silently | Add `.gitattributes` (`*.sh`, `protected-paths`, `*.yml`, `CODEOWNERS` → `eol=lf`); renormalize; add `.gitattributes` to policy as `review` | Enforcers always LF; no CRLF drift |
| M4 · M | ☐ | Fix `pre-commit` header | Comment calls CI `policy-guard` the "non-bypassable backstop"; it's advisory | Reword `.githooks/pre-commit` → "CODEOWNERS review is the gate; policy-guard is advisory" | Header matches reality |
| L1 · L | ☐ | Robust path handling | `for p in $paths` + `xargs -a` word-split → filenames with spaces mis-detected/reported | `protected-paths-check.sh`: `while IFS= read -r p` + `set -f`; `ci.yml`: `xargs -d '\n'` | Spaced paths detected/reported correctly |
| L2 · L | ☐ | Harden notifier | Bare `envsubst` expands all vars; empty `NTFY_TOPIC` → malformed URL; `"` in secret breaks YAML | `envsubst '$NTFY_TOPIC $NTFY_TOKEN'`; skip delivery if topic empty in `notify-critical.sh`; single-quote/encode the URL | Notifier robust outside CI and against odd values |
| L3 · L | ☐ | Doc cleanups | README still calls ruleset "deferred" (it's live); ADR0010 cites bare `build-test` (real checks are matrix-qualified); ADR0010/0012 say 3-field policy; label-gating temptation | README → mark ruleset live; ADR0010 → matrix check names; ADR0012 → one-line tier note; comment near `ci.yml` label step | Docs match reality; future authors warned not to gate on the label |

## Owner-verify (no code)

- [ ] **Ruleset `bypass_actors`** — confirm who's on the bypass list. Admin bypass + C1 = full collapse risk. (Settings → Rules → protect-main → Bypass list.)
- [ ] **Actions PR-run approval** — confirm whether collaborator PR workflows run automatically. Closes/confirms C2. (Settings → Actions → General.)
- [ ] **Durable bot git identity (M3)** — per-machine, not committed: set `user.name=ABox-Agent` + bot noreply email in the agent's clone so authorship doesn't fall back to `MgCohen` if the env overlay is dropped.
