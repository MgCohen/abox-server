---
status: accepted
date: 2026-06-16
amends: 0010
---

# ADR 0012 — Dependency budget scales with failure mode, not with where code runs

## Context

ADR 0010-D3 fixed the repo's enforcers as dependency-free POSIX `sh`: a flat
`glob | owner | reason` policy read by shell (since extended to
`glob | owner | tier | reason`, where `critical` rows also alert), no Node/Python/Rego toolchain,
framed as "least mechanism for blocking paths." A second kind of repo tooling is
now arriving — CI steps that *observe* rather than *enforce* (the first is a
notifier that pings when a critical path changes). These want a real library
(e.g. Apprise for multi-channel delivery) rather than hand-rolled shell, which
reads as a violation of the zero-dependency rule unless we say where the rule's
boundary actually is.

The boundary is not "shell good, libraries bad," nor "local vs CI." It is the
**failure mode** of the code:

- **Load-bearing, fails open.** If a guard does not run, a bad change can slip
  through — its absence is a security hole. Guards also run in *several*
  environments that must behave identically (the git hook on a dev box, the Claude
  `PreToolUse` adapter, the CI job), so a dependency present in one and missing in
  another silently opens that hole. Portability across seams *forces* "use only
  what is everywhere."
- **Convenience, fails safe.** If a notifier does not run, you miss a ping; the
  guard still blocked the change. Such code typically runs in one controlled
  environment (the CI runner) and degrades gracefully, so a dependency there
  carries no security cost.

## Decision

**A component's dependency budget is set by its failure mode and load-bearing
status, generalizing 0010-D3:**

- **Load-bearing / fail-open code MUST be zero-dependency and portable** — POSIX
  `sh` and standard utilities only, no install step, parseable without a library.
  This is every enforcer in the control surface (the policy reader, the git hook,
  the per-agent pre-write guard, the advisory CI guard).
- **Non-load-bearing / fail-safe code MAY take dependencies**, provided it (a)
  runs in a single controlled environment, (b) cannot, by failing or being absent,
  weaken a guarantee, and (c) does not feed its result back into a gate. Treat its
  breakage as a missed signal, not an open door.

The test for which side a component is on is one question: **if this silently
stopped running, would a protected change become *possible* (load-bearing) or
merely *unannounced* (convenience)?**

First application: the critical-path **notifier** is convenience — it runs only in
CI and a missed alert never lets a change merge — so it may use a library (Apprise)
instead of hand-rolled channel adapters. The **gate** it rides beside (the ruleset
+ CODEOWNERS, and the POSIX `protected-paths-check.sh` feeding the git hook /
PreToolUse) stays zero-dependency, unchanged.

## Consequences

- "Why is there Python in CI?" answers itself: the alert is fail-safe convenience,
  not an enforcer. A reviewer places any future tool by asking the failure-mode
  question, not by counting dependencies.
- A dependency must never migrate from the convenience side to the load-bearing
  side. If a notifier's output ever becomes a *required* check (a gate), it
  inherits the zero-dependency rule and must be reworked or kept advisory.
- The enforcement surface keeps the small, auditable, portable footprint 0010
  relies on, while non-critical tooling is free to use the right library instead
  of reinventing it in shell.

## Alternatives considered

- **Zero-dependency everywhere.** Rejected: forces hand-rolling things mature
  libraries do better (multi-channel notification), with no security benefit since
  the code cannot fail open.
- **CI may always depend, local must not.** Rejected: location is a proxy, not the
  cause. A load-bearing required check running in CI still must be portable and
  minimal; a convenience step run locally still may depend. Failure mode is the
  real axis.

## Links

- Generalizes: [`design/adr/0010-agent-repo-controls.md`](0010-agent-repo-controls.md) (D3)
- Control surface how-to: [`governance/README.md`](../../governance/README.md)
