# Critical-path alerts

Every protected-path change is labelled by tier (`review` / `attention` /
`critical-path` — see [`README`](../README.md)). On top of that label, a file marked
**`critical`** in [`protected-paths`](../policy/protected-paths) raises one extra, exclusive
signal:

- a **push notification** (via Apprise → your channels, e.g. ntfy), parallel to and
  independent of the `critical-path` label.

Detection uses the same single source of truth as the guards. The alert itself is
**fail-safe**: it can never block a merge or fail CI (ADR 0012). The merge gate is
CODEOWNERS review, which is independent of any of this.

## How it works

Runs in the **`policy-guard` job** of [`../.github/workflows/ci.yml`](../../.github/workflows/ci.yml),
on `pull_request`:

```
determine changed files
  → flag protected-path changes (advisory)
  → label  by tier  (reconcile each of review/attention/critical-path)
  → alert  (notify only if critical changed)
```

- **Detection:** `protected-paths-check.sh --tier critical` intersects the PR diff
  with policy rows tiered `critical`. Each policy row is `glob | owner | tier |
  reason`; that format and the glob semantics (`**` spans directories, `*` stays
  within a segment) are documented in the [`protected-paths`](../policy/protected-paths) header.
- **Two sinks, independent:** the label and the notification are separate steps —
  one failing never affects the other.
- **The label is a *projection*, not the authority.** The source of truth is
  `protected-paths` (tier `critical`) ∩ the diff, recomputable anywhere. Downstream
  flows MUST recompute from the policy for anything that **gates** (fail-closed);
  use the label only for routing / filtering / UI, where a miss is recoverable.

## Knobs — what's configured, and where

| To change… | Edit | How |
|---|---|---|
| Which files are critical | [`protected-paths`](../policy/protected-paths) | set an existing row's `tier` to `critical`, **or** add a new `glob \| owner \| tier \| reason` row, then run `./governance/generate-codeowners.sh` (see *Make a new path critical*) |
| The message (title / body) | [`notify-critical.sh`](notify-critical.sh) | the `title=` / `body=` lines |
| Channels + presentation | [`notify.yml`](notify.yml) | add/edit Apprise URLs |
| Channel secrets | repo Actions secrets | e.g. `NTFY_TOPIC` — low-value only in PR CI; see the secrets caveat under *Add a channel* |
| Which secrets reach the runtime | [`../.github/workflows/ci.yml`](../../.github/workflows/ci.yml) | the `Alert on critical-path changes` step's `env:` block |

## Operations

### Add a notification channel

Channel fan-out is **Apprise's** job — you add a URL, not code. Each service has its
own URL syntax; **read its page in the Apprise wiki:**
<https://github.com/caronc/apprise/wiki>.

1. In [`notify.yml`](notify.yml), add a line under `urls:` (after the existing
   `ntfy://` line). Put any secret as a `${VAR}` placeholder — it is filled from the
   environment at runtime; **never commit a secret.** Example (Discord):
   ```yaml
   - "discord://${DISCORD_WEBHOOK_ID}/${DISCORD_WEBHOOK_TOKEN}"
   ```
2. Add the matching secret(s) under repo **Settings → Secrets and variables →
   Actions**.
3. Wire those secrets into the **`Alert on critical-path changes`** step's `env:`
   block in [`../.github/workflows/ci.yml`](../../.github/workflows/ci.yml) — the step
   that already has `NTFY_TOPIC`, *not* the label step — so the `${VAR}` placeholders
   expand at runtime:
   ```yaml
   env:
     DISCORD_WEBHOOK_ID: ${{ secrets.DISCORD_WEBHOOK_ID }}
     DISCORD_WEBHOOK_TOKEN: ${{ secrets.DISCORD_WEBHOOK_TOKEN }}
   ```

That is the whole operation — no per-channel code, because Apprise owns the
integrations.

> **⚠️ Secrets in PR-triggered CI (C2 / [ADR 0012](../decisions/0012-dependency-budget-by-failure-mode.md)).**
> This step runs on `pull_request`, including PRs authored by the agent. A secret
> wired here is readable by that PR's workflow *before* an owner approves it. Only a
> **low-value** secret belongs here — `NTFY_TOPIC` is an unguessable string, not a
> credential, so it is safe. For an **authenticated** channel (an API token, a
> webhook secret, `NTFY_TOKEN`), do **not** add it to this step: gate it behind a
> protected **Environment** (required reviewer) or move delivery to a **post-merge**
> `push`-triggered job, so the credential is never exposed to an unreviewed PR.

### Change the message

The message is built in [`notify-critical.sh`](notify-critical.sh) (`title=` /
`body=`). Variables already available: `${GITHUB_REPOSITORY}`, `${PR_URL}`, and
`$hits` (the changed critical files).

To add a **new** field, wire it in **two places** — the env-var name in `ci.yml` and
the `$VAR` in the script must match exactly; that pairing is what makes the field
appear at runtime:

1. In [`../.github/workflows/ci.yml`](../../.github/workflows/ci.yml), add it to the
   `Alert on critical-path changes` step's `env:`, mapping a GitHub event field to an
   env var. Common fields:
   - PR number → `PR_NUMBER: ${{ github.event.pull_request.number }}`
   - PR author login → `PR_AUTHOR: ${{ github.event.pull_request.user.login }}`
   - source branch → `PR_BRANCH: ${{ github.event.pull_request.head.ref }}`
2. In [`notify-critical.sh`](notify-critical.sh), reference the same var in `body=`
   (or `title=`). Use the `${VAR:+ …}` form so the field is omitted when the var is
   empty, matching the existing `${PR_URL:+ ($PR_URL)}`:
   ```sh
   body="Critical files changed${PR_URL:+ ($PR_URL)}${PR_NUMBER:+ — PR #$PR_NUMBER}${PR_AUTHOR:+ by @$PR_AUTHOR}:
   $(printf '%s\n' "$hits" | sed 's/^/- /')"
   ```

### Make a new path critical (alert + label)

1. Add a row to [`protected-paths`](../policy/protected-paths) in the format
   `glob | owner | tier | reason`, with `tier = critical`. (`**` spans directories,
   `*` stays within a segment. To make an *existing* path critical, just change its
   `tier` to `critical` — no new row.)
2. Regenerate the owner map — never hand-edit `CODEOWNERS`:
   ```sh
   ./governance/generate-codeowners.sh
   ```
   Adding any protected path without this leaves it with no code owner / merge gate.

Use `tier = review` instead for gate-only (code-owner review, no alert).

### Tune ntfy loudness / format

Edit the URL params in [`notify.yml`](notify.yml): `priority` (`min`…`max`), `tags`
(emoji shortcodes), `format=markdown`.

### Receive on your phone

Install the ntfy app, subscribe to your topic, and set the `NTFY_TOPIC` secret. For
a private/reserved topic, use a token and the form
`ntfy://${NTFY_TOKEN}@ntfy.sh/${NTFY_TOPIC}` — but `NTFY_TOKEN` is a credential, so
wire it per the secrets caveat above (protected Environment or post-merge job), not
into the PR-triggered alert step.

### Test it

Open or update a PR that touches a critical path (or re-run the `policy-guard` job),
and watch the **`Alert on critical-path changes`** step. Apprise is **silent on
success** — no error means it sent. To validate a channel URL offline before
relying on CI, run it locally with real values, e.g.
`apprise -t test -b test 'discord://id/token'`.

## Troubleshooting

- **No push received** — the ntfy app must be subscribed to the *exact* topic in
  `NTFY_TOPIC` (case-sensitive). Check the alert step log: it lists the detected
  files and any delivery error.
- **`Invalid … TEXT configuration … version: 1`** — Apprise picks its parser from
  the file extension; `notify-critical.sh` renders the config to a `.yml` temp file
  for exactly this reason. Keep that.
- **`apprise unavailable`** — the runner's `pipx`/`pip` install failed; the step
  skips delivery (non-blocking).

## Files

- [`protected-paths`](../policy/protected-paths) — which paths, which tier (`glob | owner | tier | reason`).
- [`protected-paths-check.sh`](protected-paths-check.sh) — the shared checker; `--tier <name>` lists matches of a tier.
- [`generate-codeowners.sh`](generate-codeowners.sh) — regenerate `.github/CODEOWNERS` after editing the policy.
- [`../.github/CODEOWNERS`](../../.github/CODEOWNERS) — generated owner map (do not hand-edit).
- [`notify.yml`](notify.yml) — channels (Apprise URLs).
- [`notify-critical.sh`](notify-critical.sh) — detection + message + delivery.
- [`../.github/workflows/ci.yml`](../../.github/workflows/ci.yml) — the `policy-guard` job that runs it.

See [ADR 0012](../decisions/0012-dependency-budget-by-failure-mode.md) for why the
alert may use a dependency while the guards may not.
