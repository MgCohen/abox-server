#!/usr/bin/env node
// Spike harness for spikes/morph-completion-cascade/index.html.
// Proves the two silent-failure mechanisms from the Morph refactor WITHOUT any
// JS in the page: the harness sets data-phase and tallies animationend, which is
// exactly what the C# engine does. A hang or a miscount fails the run.
//
//   node probe-morph.mjs [pathToHtml]
//
import { chromium } from "playwright-core";
import { pathToFileURL } from "url";
import { mkdirSync } from "fs";

const HTML = process.argv[2]
  ?? `${import.meta.dirname}/../../spikes/morph-completion-cascade/index.html`;
const URL = pathToFileURL(HTML).href;

const EXPECTED_ITEMS = 3;          // raised, inset, cutout
const SENTINEL = "morph-sentinel";
const SETTLE_MS = 2200;            // > slowest item (1100ms) + delays + margin
const TOL = 70;                    // timing tolerance (ms) for jittery CI

async function launch() {
  for (const channel of ["chrome", "msedge"]) {
    try { return await chromium.launch({ channel, headless: true }); } catch {}
  }
  return await chromium.launch({ headless: true });
}

function durMs(s) {                 // "0.5s, 0.5s" | "900ms" -> 500 | 900
  const first = String(s).split(",")[0].trim();
  if (first.endsWith("ms")) return parseFloat(first);
  if (first.endsWith("s")) return parseFloat(first) * 1000;
  return parseFloat(first) || 0;
}

const checks = [];
const ok = (name, pass, detail) => checks.push({ name, pass: !!pass, detail });

async function main() {
  const out = `${import.meta.dirname}/artifacts/morph-spike`;
  mkdirSync(out, { recursive: true });

  const browser = await launch();
  const page = await browser.newPage({ viewport: { width: 900, height: 700 } });
  const consoleErrors = [];
  page.on("console", (m) => m.type() === "error" && consoleErrors.push(m.text()));
  page.on("pageerror", (e) => consoleErrors.push("pageerror: " + e));

  // Install the listener BEFORE any content loads. animationend bubbles, so one
  // capturing listener on the document sees every item + inner-layer event.
  await page.addInitScript(() => {
    window.__events = [];
    document.addEventListener("animationend", (e) => {
      window.__events.push({
        name: e.animationName,
        cls: (e.target.getAttribute && e.target.getAttribute("data-name")) || e.target.className || "",
        t: performance.now(),
      });
    }, true);
  });

  await page.goto(URL, { waitUntil: "load" });
  await page.screenshot({ path: `${out}/00-idle.png`, fullPage: true });

  // Flip the phase (what C# does) and stamp t0 in the same tick.
  await page.evaluate(() => {
    window.__t0 = performance.now();
    document.querySelector(".morph-stage").setAttribute("data-phase", "enter");
  });

  // Computed durations PROVE conflict B: each item resolves its OWN --enter-dur.
  const durations = await page.evaluate(() =>
    Object.fromEntries([...document.querySelectorAll(".morph-item")].map((el) => [
      el.getAttribute("data-name"),
      getComputedStyle(el).animationDuration,
    ]))
  );

  await page.waitForTimeout(600);
  await page.screenshot({ path: `${out}/01-mid.png`, fullPage: true });
  await page.waitForTimeout(SETTLE_MS - 600);
  await page.screenshot({ path: `${out}/02-settled.png`, fullPage: true });

  const { events, t0 } = await page.evaluate(() => ({ events: window.__events, t0: window.__t0 }));
  await browser.close();

  const rel = events.map((e) => ({ ...e, ms: Math.round(e.t - t0) }));
  const sentinels = rel.filter((e) => e.name === SENTINEL);
  const others = rel.filter((e) => e.name !== SENTINEL);
  const byItem = (n) => sentinels.find((e) => e.cls === n);

  // --- A1: exactly one counted (sentinel) event per .morph-item -> no hang, no inflation
  ok("A · sentinel count == item count (no hang / no inflation)",
     sentinels.length === EXPECTED_ITEMS,
     `${sentinels.length} sentinel events vs ${EXPECTED_ITEMS} items`);

  // --- A2: each style's item emitted its sentinel
  ok("A · every style's item counted (raised/inset/cutout)",
     ["raised", "inset", "cutout"].every(byItem),
     ["raised", "inset", "cutout"].map((n) => `${n}:${byItem(n) ? "✓" : "MISSING"}`).join(" "));

  // --- A3: inner-layer + content animations fired but were NOT counted
  const innerNames = new Set(others.map((e) => e.name));
  ok("A · inner/content events present but excluded from the tally",
     others.length > 0 && !innerNames.has(SENTINEL),
     `${others.length} non-counted events: ${[...innerNames].join(", ")}`);

  // --- B1: each item resolved its OWN duration (override works in a mixed stage)
  const dR = durMs(durations.raised), dI = durMs(durations.inset), dC = durMs(durations.cutout);
  ok("B · per-subtree duration override (raised 500 / inset 900 / cutout 1100)",
     Math.abs(dR - 500) < 30 && Math.abs(dI - 900) < 30 && Math.abs(dC - 1100) < 30,
     `raised=${dR}ms inset=${dI}ms cutout=${dC}ms (computed)`);

  // --- B2: the sentinel actually COMPLETED at each item's own duration
  const near = (v, target) => v != null && Math.abs(v - target) < TOL + 40;
  ok("B · sentinels completed at their own durations",
     near(byItem("raised")?.ms, 500) && near(byItem("inset")?.ms, 960) && near(byItem("cutout")?.ms, 1100),
     `raised@${byItem("raised")?.ms} inset@${byItem("inset")?.ms} cutout@${byItem("cutout")?.ms} ms`);

  // --- Q4: no EARLY resolve — cut-out sentinel finishes no sooner than its visible motion
  const cutoutInner = others.filter((e) => e.cls === "cutout" || /cutout|content/.test(e.name));
  const maxInner = cutoutInner.length ? Math.max(...cutoutInner.map((e) => e.ms)) : 0;
  ok("Q4 · sentinel ≥ longest visible motion (no early resolve)",
     (byItem("cutout")?.ms ?? 0) >= maxInner - TOL,
     `cutout sentinel@${byItem("cutout")?.ms}ms vs longest inner@${maxInner}ms`);

  // --- stagger sanity: inset (delayed + longer) settles after raised
  ok("B · stagger preserved (inset settles after raised)",
     (byItem("inset")?.ms ?? 0) > (byItem("raised")?.ms ?? 0),
     `raised@${byItem("raised")?.ms}ms  inset@${byItem("inset")?.ms}ms`);

  // --- no console/page errors
  ok("no console/page errors", consoleErrors.length === 0, consoleErrors.slice(0, 3).join(" | ") || "clean");

  console.log("\n=== morph-completion-cascade spike ===");
  console.log(`url: ${URL}`);
  console.log(`computed durations: ${JSON.stringify(durations)}`);
  console.log(`events (${rel.length}): ` + rel.map((e) => `${e.name}@${e.ms}ms[${e.cls}]`).join(", "));
  console.log("");
  for (const c of checks) console.log(`  [${c.pass ? "PASS" : "FAIL"}] ${c.name}  — ${c.detail}`);
  const failed = checks.filter((c) => !c.pass);
  console.log(`\nRESULT: ${failed.length ? "FAIL" : "PASS"}  (${checks.length - failed.length}/${checks.length})`);
  console.log(`screenshots: ${out}`);
  process.exit(failed.length ? 1 : 0);
}

main().catch((e) => { console.error(e); process.exit(2); });
