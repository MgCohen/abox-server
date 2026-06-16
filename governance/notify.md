# Critical-path alerts

When a PR changes a file marked **`critical`** in [`protected-paths`](protected-paths),
CI raises two **independent, parallel** signals:

1. a **`critical-path` label** on the PR (a routing / visibility marker), and
2. a **push notification** (via Apprise → your channels, e.g. ntfy).

Detection uses the same single source of truth as the guards. The alert itself is
**fail-safe**: it can never block a merge or fail CI (ADR 0012). The merge gate is
CODEOWNERS review, which is independent of any of this.

## How it works

Runs in the **`policy-guard` job** of [`../.github/workflows/ci.yml`](../.github/workflows/ci.yml),
on `pull_request`:

```
determine changed files
  → flag protected-path changes (advisory)
  → label  critical-path  (reconcile: add if critical changed, remove if not)
  → alert  (notify only if critical changed)
```

- **Detection:** `protected-paths-check.sh --tier critical` intersects the PR diff
  with policy rows tiered `critical`.
- **Two sinks, independent:** the label and the notification are separate steps —
  one failing never affects the other.
- **The label is a *projection*, not the authority.** The source of truth is
  `protected-paths` (tier `critical`) ∩ the diff, recomputable anywhere. Downstream
  flows MUST recompute from the policy for anything that **gates** (fail-closed);
  use the label only for routing / filtering / UI, where a miss is recoverable.

## Knobs — what's configured, and where

| To change… | Edit | How |
|---|---|---|
| Which files are critical | [`protected-paths`](protected-paths) | set the `tier` column to `critical` (or `review` for gate-only) |
| The message (title / body) | [`notify-critical.sh`](notify-critical.sh) | the `title=` / `body=` lines |
| Channels + presentation | [`notify.yml`](notify.yml) | add/edit Apprise URLs |
| Channel secrets | repo Actions secrets | e.g. `NTFY_TOPIC`, `NTFY_TOKEN` |
| Which secrets reach the runtime | [`../.github/workflows/ci.yml`](../.github/workflows/ci.yml) | the alert step's `env:` block |

## Operations

### Add a notification channel

Channel fan-out is **Apprise's** job — you add a URL, not code. Each service has its
own URL syntax; **read its page in the Apprise wiki:**
<https://github.com/caronc/apprise/wiki>.

1. Add a line under `urls:` in [`notify.yml`](notify.yml). Put any secret as a
   `${VAR}` placeholder — it is filled from the environment at runtime; **never
   commit a secret.** Example (Discord):
   ```yaml
   - "discord://${DISCORD_WEBHOOK_ID}/${DISCORD_WEBHOOK_TOKEN}"
   ```
2. Add the matching secret(s) under repo **Settings → Secrets and variables →
   Actions**.
3. Wire those secrets into the alert step's `env:` in
   [`../.github/workflows/ci.yml`](../.github/workflows/ci.yml) so the runtime sees
   them (this is what makes `${VAR}` expand):
   ```yaml
   env:
     DISCORD_WEBHOOK_ID: ${{ secrets.DISCORD_WEBHOOK_ID }}
     DISCORD_WEBHOOK_TOKEN: ${{ secrets.DISCORD_WEBHOOK_TOKEN }}
   ```
   (`NTFY_TOPIC` / `NTFY_TOKEN` are already wired as the worked example.)

That is the whole operation — no per-channel code, because Apprise owns the
integrations.

### Change the message

Edit `title=` / `body=` in [`notify-critical.sh`](notify-critical.sh). Variables
already available: `${GITHUB_REPOSITORY}`, `${PR_URL}`, and `$hits` (the changed
critical files). To add a new field (PR number, author, branch): add it to the alert
step's `env:` in `ci.yml` from `${{ github.event.pull_request.* }}`, then reference
it in `notify-critical.sh`.

### Tune ntfy loudness / format

Edit the URL params in [`notify.yml`](notify.yml): `priority` (`min`…`max`), `tags`
(emoji shortcodes), `format=markdown`.

### Receive on your phone

Install the ntfy app, subscribe to your topic, and set the `NTFY_TOPIC` secret. For
a private/reserved topic, use a token and the form
`ntfy://${NTFY_TOKEN}@ntfy.sh/${NTFY_TOPIC}`.

### Test it

Open or update a PR that touches a critical path (or re-run the `policy-guard` job),
and watch the **"Alert on critical-path changes"** step. Apprise is **silent on
success** — no error means it sent.

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

- [`protected-paths`](protected-paths) — which paths, which tier.
- [`notify.yml`](notify.yml) — channels (Apprise URLs).
- [`notify-critical.sh`](notify-critical.sh) — detection + message + delivery.
- [`../.github/workflows/ci.yml`](../.github/workflows/ci.yml) — the `policy-guard` job that runs it.

See [ADR 0012](../design/adr/0012-dependency-budget-by-failure-mode.md) for why the
alert may use a dependency while the guards may not.
