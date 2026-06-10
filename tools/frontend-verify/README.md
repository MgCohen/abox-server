# frontend-verify

Headless **real-browser** checks for the repo's Blazor frontends. The agent's
in-tool preview is blind to CSS animations and runs no WASM, so anything past
"does the HTML parse" — runtime rendering, console/page errors, animation
motion — needs an actual browser. This is that browser.

It drives the **system Chrome or Edge** (Playwright `channel`), so there's no
~150 MB browser download. Only the `playwright-core` npm package is fetched.

## One-time setup

```
cd tools/frontend-verify
npm install          # pulls playwright-core only — no browser download
```

Requires a system Chrome or Edge (both ship on this Windows host) and Node 18+.

## Use

Point it at a running URL, or let it launch a project for you with `--serve`.

```bash
# Launch a Blazor project, wait for a selector, screenshot it, check for errors
node verify.mjs http://localhost:5249/ \
  --serve ../../spikes/morph-demo/morph-demo.csproj \
  --wait "button.swap" --shot home

# Capture an animation as a frame burst (name:frames:stepMs)
node verify.mjs http://localhost:5249/ --burst swap:9:110

# Emulate prefers-reduced-motion (verify motion is skipped, not stalled)
node verify.mjs http://localhost:5249/ --reduced --shot reduced

# Watch it live instead of headless
node verify.mjs http://localhost:5249/ --headed --wait "button.swap"
```

### Flags

| Flag | Effect |
|------|--------|
| `<url>` | Page to open (required, first positional arg). |
| `--serve <csproj>` | `dotnet run` this project, wait for the URL, tear it down after. |
| `--wait <selector>` | Fail unless the selector becomes visible. Repeatable. |
| `--shot <name>` | Full-page screenshot → `artifacts/<run>/<name>.png`. Repeatable. |
| `--burst <name:frames:stepMs>` | Rapid screenshots for animation frames. Repeatable. |
| `--reduced` | Emulate `prefers-reduced-motion: reduce`. |
| `--headed` | Show the browser instead of headless. |
| `--strict` | Also fail on console errors / HTTP ≥ 400 (not just uncaught page errors). |
| `--channel <chrome\|msedge>` | Force a browser; default tries chrome then msedge. |
| `--viewport <WxH>` | Default `1024x768`. |
| `--timeout <ms>` | Nav / readiness / selector timeout. Default `30000`. |
| `--out <dir>` | Artifact dir. Default `artifacts/<timestamp>/`. |

Screenshots land under `artifacts/` (git-ignored). Read the PNGs to inspect
rendering or step through animation frames.

### Result / exit code

Always reports page errors, console errors, and failed responses. Exits non-zero
when an **uncaught page error** or a **`--wait` miss** occurs (hard failures), or
on any console error / bad response under `--strict`. Use it as a CI-style gate
or just read the report.

## Writing a custom interaction

For multi-step flows (click, type, assert) beyond the flags, copy `verify.mjs`
as a starting point or import `playwright-core` directly — `chromium.launch({ channel: "chrome" })`
is the only repo-specific bit.
