#!/usr/bin/env node
// Spike harness for spikes/morph-getanimations/index.html.
// Defines the EXACT production `waitForAnimations` helper (the ~5-liner we'd ship
// in morph.js) and drives three completion cases. The page stays script-free; all
// logic lives here, mirroring what the C# engine will await over JS interop.
//
//   node probe-getanimations.mjs [pathToHtml]
//
import { chromium } from "playwright-core";
import { pathToFileURL } from "url";
import { mkdirSync } from "fs";

const HTML = process.argv[2]
  ?? `${import.meta.dirname}/../../spikes/morph-getanimations/index.html`;
const URL = pathToFileURL(HTML).href;

async function launch() {
  for (const channel of ["chrome", "msedge"]) {
    try { return await chromium.launch({ channel, headless: true }); } catch {}
  }
  return await chromium.launch({ headless: true });
}

const checks = [];
const ok = (name, pass, detail) => checks.push({ name, pass: !!pass, detail });

async function main() {
  const out = `${import.meta.dirname}/artifacts/getanimations-spike`;
  mkdirSync(out, { recursive: true });

  const browser = await launch();
  const page = await browser.newPage({ viewport: { width: 700, height: 480 } });
  const errors = [];
  page.on("console", (m) => m.type() === "error" && errors.push(m.text()));
  page.on("pageerror", (e) => errors.push("pageerror: " + e));

  // Inject the production helper + a parallel start/end COUNTER, so we can show
  // getAnimations is correct exactly where counting is not.
  await page.addInitScript(() => {
    // The exact shape we'd ship in morph.js:
    window.waitForAnimations = (el) =>
      new Promise((resolve) => requestAnimationFrame(() => {
        const anims = el.getAnimations({ subtree: true })
          .filter((a) => a.effect?.getComputedTiming().iterations !== Infinity);
        resolve(Promise.all(anims.map((a) => a.finished)));
      }));

    // Parallel animationstart/animationend balance counter (the C#-only rival).
    window.__c = null;
    document.addEventListener("animationstart", () => { const c = window.__c; if (c) c.started++; }, true);
    document.addEventListener("animationend", () => {
      const c = window.__c; if (!c) return;
      c.ended++;
      if (c.started > 0 && c.started === c.ended && c.firstMatch == null) c.firstMatch = performance.now() - c.t0;
    }, true);
  });

  await page.goto(URL, { waitUntil: "load" });

  // --- Case 1: GAPPED stagger. getAnimations must wait for B (delayed past A's end);
  //     the start/end counter would have "completed" at ~200ms.
  const gap = await page.evaluate(async () => {
    const stage = document.getElementById("gap");
    window.__c = { started: 0, ended: 0, firstMatch: null, t0: performance.now() };
    const t0 = performance.now();
    stage.setAttribute("data-phase", "enter");
    await window.waitForAnimations(stage);
    return { resolveMs: Math.round(performance.now() - t0), countMatchMs: window.__c.firstMatch == null ? null : Math.round(window.__c.firstMatch) };
  });
  ok("gapped stagger · getAnimations waited for the delayed layer (~800ms)",
     gap.resolveMs >= 760 && gap.resolveMs <= 1100, `resolved @${gap.resolveMs}ms`);
  ok("gapped stagger · start/end COUNTING would have false-completed early (~200ms)",
     gap.countMatchMs != null && gap.countMatchMs < 400 && gap.resolveMs - gap.countMatchMs > 300,
     `counter first matched @${gap.countMatchMs}ms vs true end @${gap.resolveMs}ms`);

  // --- Case 2: INTERRUPTION. Start the wait, cancel the animation mid-flight,
  //     .finished must reject AbortError (no hang, no crash).
  await page.evaluate(() => {
    const stage = document.getElementById("long");
    window.__interrupt = null;
    stage.setAttribute("data-phase", "enter");
    window.waitForAnimations(stage).then(
      () => (window.__interrupt = { status: "resolved" }),
      (e) => (window.__interrupt = { status: "rejected", name: e?.name ?? String(e) }),
    );
  });
  await page.waitForTimeout(200);
  await page.evaluate(() => document.getElementById("long").setAttribute("data-phase", "")); // cancel
  await page.waitForTimeout(200);
  const intr = await page.evaluate(() => window.__interrupt);
  ok("interruption · .finished rejected cleanly (AbortError), no hang",
     intr?.status === "rejected" && /Abort/i.test(intr?.name ?? ""),
     `result: ${JSON.stringify(intr)}`);

  // Re-run the same element to prove a fresh phase still completes after an interrupt.
  const reRun = await page.evaluate(async () => {
    const stage = document.getElementById("long");
    const t0 = performance.now();
    stage.setAttribute("data-phase", "enter");
    await window.waitForAnimations(stage);
    return Math.round(performance.now() - t0);
  });
  ok("interruption · a fresh phase completes normally afterward (~800ms)",
     reRun >= 760 && reRun <= 1100, `resolved @${reRun}ms`);

  // --- Case 3: STATIC phase. No animations → empty set → resolve immediately.
  const stat = await page.evaluate(async () => {
    const stage = document.getElementById("static");
    const t0 = performance.now();
    stage.setAttribute("data-phase", "enter");
    await window.waitForAnimations(stage);
    return Math.round(performance.now() - t0);
  });
  ok("static phase · empty animation set resolved instantly (no hang)",
     stat < 100, `resolved @${stat}ms`);

  ok("no console/page errors", errors.length === 0, errors.slice(0, 3).join(" | ") || "clean");

  await browser.close();

  console.log("\n=== morph-getanimations spike ===");
  console.log(`url: ${URL}`);
  console.log(`gap: ${JSON.stringify(gap)}  interrupt: ${JSON.stringify(intr)}  reRun: ${reRun}ms  static: ${stat}ms`);
  console.log("");
  for (const c of checks) console.log(`  [${c.pass ? "PASS" : "FAIL"}] ${c.name}  — ${c.detail}`);
  const failed = checks.filter((c) => !c.pass);
  console.log(`\nRESULT: ${failed.length ? "FAIL" : "PASS"}  (${checks.length - failed.length}/${checks.length})`);
  process.exit(failed.length ? 1 : 0);
}

main().catch((e) => { console.error(e); process.exit(2); });
