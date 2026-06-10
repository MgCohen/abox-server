#!/usr/bin/env node
import { chromium } from "playwright-core";
import { spawn } from "child_process";
import { mkdirSync } from "fs";
import { setTimeout as sleep } from "timers/promises";

function parseArgs(argv) {
  const opts = {
    url: null,
    serve: null,
    waits: [],
    shots: [],
    bursts: [],
    reduced: false,
    headed: false,
    strict: false,
    viewport: { width: 1024, height: 768 },
    timeout: 30000,
    channel: null,
    out: null,
  };
  const rest = [...argv];
  while (rest.length) {
    const a = rest.shift();
    switch (a) {
      case "--serve": opts.serve = rest.shift(); break;
      case "--wait": opts.waits.push(rest.shift()); break;
      case "--shot": opts.shots.push(rest.shift()); break;
      case "--burst": opts.bursts.push(rest.shift()); break;
      case "--reduced": opts.reduced = true; break;
      case "--headed": opts.headed = true; break;
      case "--strict": opts.strict = true; break;
      case "--timeout": opts.timeout = Number(rest.shift()); break;
      case "--channel": opts.channel = rest.shift(); break;
      case "--out": opts.out = rest.shift(); break;
      case "--viewport": {
        const [w, h] = rest.shift().split("x").map(Number);
        opts.viewport = { width: w, height: h };
        break;
      }
      default:
        if (a.startsWith("--")) throw new Error(`Unknown flag: ${a}`);
        opts.url = a;
    }
  }
  if (!opts.url) throw new Error("Usage: node verify.mjs <url> [--serve <csproj>] [--wait sel] [--shot name] [--burst name:frames:stepMs] [--reduced] [--headed] [--strict]");
  return opts;
}

async function launchBrowser(opts) {
  const channels = opts.channel ? [opts.channel] : ["chrome", "msedge"];
  for (const channel of channels) {
    try {
      const browser = await chromium.launch({ channel, headless: !opts.headed });
      return { browser, channel };
    } catch {
      // try the next system browser
    }
  }
  // last resort: a bundled chromium, if one was ever installed
  const browser = await chromium.launch({ headless: !opts.headed });
  return { browser, channel: "bundled-chromium" };
}

async function waitForHttp(url, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      const res = await fetch(url);
      if (res.ok) return;
    } catch {
      // server not up yet
    }
    await sleep(500);
  }
  throw new Error(`Server at ${url} did not become ready within ${timeoutMs}ms`);
}

function startServer(csproj, url) {
  const child = spawn("dotnet", ["run", "--project", csproj, "--urls", url], {
    stdio: "ignore",
    shell: false,
  });
  return child;
}

function stopServer(child) {
  if (!child || child.killed) return;
  if (process.platform === "win32") {
    spawn("taskkill", ["/pid", String(child.pid), "/t", "/f"], { stdio: "ignore" });
  } else {
    child.kill("SIGTERM");
  }
}

async function main() {
  const opts = parseArgs(process.argv.slice(2));
  const stamp = new Date().toISOString().replace(/[:.]/g, "-");
  const out = opts.out ?? `${import.meta.dirname}/artifacts/${stamp}`;
  mkdirSync(out, { recursive: true });

  let server = null;
  if (opts.serve) {
    server = startServer(opts.serve, opts.url);
    await waitForHttp(opts.url, opts.timeout);
  }

  const consoleErrors = [];
  const pageErrors = [];
  const badResponses = [];

  const { browser, channel } = await launchBrowser(opts);
  console.log(`browser: ${channel}  url: ${opts.url}  out: ${out}`);

  const context = await browser.newContext({
    viewport: opts.viewport,
    reducedMotion: opts.reduced ? "reduce" : "no-preference",
  });
  const page = await context.newPage();
  page.on("console", (m) => { if (m.type() === "error") consoleErrors.push(m.text()); });
  page.on("pageerror", (e) => pageErrors.push(String(e)));
  page.on("response", (r) => { if (r.status() >= 400) badResponses.push(`${r.status()} ${r.url()}`); });

  let failures = [];
  try {
    await page.goto(opts.url, { waitUntil: "networkidle", timeout: opts.timeout });

    for (const sel of opts.waits) {
      try {
        await page.waitForSelector(sel, { state: "visible", timeout: opts.timeout });
      } catch {
        failures.push(`selector never became visible: ${sel}`);
      }
    }

    for (const name of opts.shots) {
      await page.screenshot({ path: `${out}/${name}.png`, fullPage: true });
    }

    for (const spec of opts.bursts) {
      const [name, framesRaw, stepRaw] = spec.split(":");
      const frames = Number(framesRaw ?? 9);
      const step = Number(stepRaw ?? 100);
      for (let i = 0; i < frames; i++) {
        await page.screenshot({ path: `${out}/${name}-${String(i).padStart(2, "0")}.png` });
        await sleep(step);
      }
    }
  } catch (e) {
    failures.push(`navigation/interaction error: ${e.message}`);
  } finally {
    await browser.close();
    stopServer(server);
  }

  const report = (label, items) => {
    if (!items.length) { console.log(`  ${label}: none`); return; }
    console.log(`  ${label}: ${items.length}`);
    for (const it of items.slice(0, 20)) console.log(`    - ${it}`);
  };
  console.log("--- frontend-verify report ---");
  report("page errors", pageErrors);
  report("console errors", consoleErrors);
  report("failed responses (>=400)", badResponses);
  report("check failures", failures);

  const hard = pageErrors.length + failures.length;
  const soft = consoleErrors.length + badResponses.length;
  const failed = hard > 0 || (opts.strict && soft > 0);
  console.log(failed ? "RESULT: FAIL" : "RESULT: PASS");
  process.exit(failed ? 1 : 0);
}

main().catch((e) => { console.error(e.message); process.exit(2); });
