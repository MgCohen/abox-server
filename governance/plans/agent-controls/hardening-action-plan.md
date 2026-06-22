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
| C2 · H | ☑ | Keep sensitive secrets out of PR-triggered CI | Bot can author `ci.yml` on a PR branch; same-repo PRs run with secrets *before* merge → exfil | Document the constraint: only low-value `NTFY_TOPIC` in PR-triggered steps, never a sensitive secret; owner verifies Actions → PR-run approval setting. *Deferred note (don't build now):* when a sensitive channel is first added, gate it behind a protected **Environment** (required reviewer) or a post-merge `push` job | No meaningful secret reachable by a bot-authored PR workflow; mechanism added only on the second real use |
| H1 · H | ☑ | Make the `.claude` PreToolUse guard real, or drop the claims | It's git-ignored/absent, yet ADR0010 + README + policy call it a live enforcer → CODEOWNERS row inert, "can't disable itself" is false | **Struck the phantom** (see Resolution): removed the `.claude/settings.json` policy row + the PreToolUse/SessionStart claims from ADR0010 & README | Docs stop claiming a phantom; building a real guard offered as a follow-up |
| H2 · H | ☑ | Protect the arch/structure enforcer *code* | Rulebook markdown is protected but `tests/Tests/Arch/**` & `Structure/**` (the asserting tests) are freely editable → agent can weaken a rule to pass | Added `tests/Tests/Arch/** \| @MgCohen \| critical` and `tests/Tests/Structure/** \| @MgCohen \| critical`; regenerated CODEOWNERS | Weakening a structural assertion needs owner review + alert |
| M1 · M | ☑ | Resync CODEOWNERS | Committed file drifted from generator (row order) → "always regenerate" invariant already broken | Regenerated `CODEOWNERS`; added a `policy-guard` step asserting generator output == committed (fails red on drift) | CODEOWNERS == generator output; drift fails CI |
| M2 · M | ☑ | Add `.gitattributes` (LF) | Enforcer `sh` scripts can come from a CRLF Windows checkout, run on Linux runners → one commit breaks a guard silently | Added `.gitattributes` (`*.sh`, `*.yml`, `protected-paths`, `CODEOWNERS`, `.githooks/*` → `eol=lf`); added `.gitattributes` to policy as `review` | Enforcers always LF; no CRLF drift |
| M4 · M | ☑ | Fix `pre-commit`/`pre-push` headers | Comments call CI `policy-guard` the "non-bypassable backstop"; it's advisory | Reworded both hooks → "CODEOWNERS review is the gate; policy-guard is advisory" | Headers match reality |
| L1 · L | ☑ | Robust path handling | `for p in $paths` + `xargs -a` word-split → filenames with spaces mis-detected/reported | `protected-paths-check.sh`: `while IFS= read -r p` + `set -f`; `ci.yml`: feed the checker via stdin (`< changed.txt`) instead of `xargs` (see Resolution) | Spaced paths detected/reported correctly |
| L2 · L | ☑ | Harden notifier | Bare `envsubst` expands all vars; a literal `$` in a channel URL would be eaten | `envsubst` scoped to only the `${VAR}` placeholders the config declares, derived generically so the dispatcher stays channel-agnostic (see Resolution) | Notifier robust against odd values without hardcoding a channel's var names |
| L3 · L | ☑ | Doc cleanups | README called ruleset "deferred" (it's live); ADR0010 cited bare `build-test`; ADR0012 said 3-field policy; label-gating temptation | README → ruleset/identity marked live; ADR0010 → matrix check names; ADR0012 → tier note; label-is-a-projection comment in `ci.yml` | Docs match reality; future authors warned not to gate on the label |

### Resolution notes (where the build differed from the literal plan)

- **H1 — struck, not built.** Chose the scope-conservative path: the PreToolUse
  deny is design-classed as non-load-bearing "earliest feedback" (ADR 0012) and
  `.claude/settings.json`+`hooks/` are gitignored/absent. Building a real runtime
  guard is a new feature with per-clone effects — offered as a follow-up, not folded
  into a hardening PR.
- **L1 — stdin, not `xargs -d`.** Since the checker's stdin mode is now newline-safe,
  `ci.yml` feeds it `< changed.txt` (cleaner than the GNU-only `xargs -d '\n'`).
  Also fixed a `set -e` regression: the `--tier` list loop was rewritten with
  `if/fi` so it reliably exits 0 (a `printf | while` pipeline had aborted under
  `set -e`).
- **L2 — generic scoping, no `NTFY_TOPIC` hardcode.** The plan said
  `envsubst '$NTFY_TOPIC $NTFY_TOKEN'`, but naming a channel's vars in the generic
  dispatcher would break the decoupled-channels design. Instead the scope is derived
  from the `${VAR}`s present in `notify.yml`. The empty-topic skip stays in CI (which
  already guards it); apprise fails safe on a bad URL.

## Owner-verify (no code)

- [ ] **Ruleset `bypass_actors`** — confirm who's on the bypass list. Admin bypass + C1 = full collapse risk. (Settings → Rules → protect-main → Bypass list.)
- [ ] **Actions PR-run approval** — confirm whether collaborator PR workflows run automatically. Closes/confirms C2. (Settings → Actions → General.)
- [ ] **Durable bot git identity (M3)** — per-machine, not committed: set `user.name=ABox-Agent` + bot noreply email in the agent's clone so authorship doesn't fall back to `MgCohen` if the env overlay is dropped.
