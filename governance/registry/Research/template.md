# Research Artifact

## Purpose

Reach for Research when a decision needs grounded, multi-source evidence — not a quick lookup but a
fact-checked, cited report that outlives the question and that others can audit.

A Research report investigates one question by fanning out across sources, verifying claims against them, and
synthesizing a cited answer. It is nl-first and advisory (`gate: advise`): it informs a decision or a later
ADR/plan, and never gates a build — so unlike the Test artifact it carries no Rules and no parity. Instances
live under the artifact's `home`; quality is graded by `/judge` against the criteria below, since "is this
good research" is a semantic call no mechanical guard can make.

## Criteria

- **sourced:** every non-obvious claim cites a locatable source (link or reference), not an unattributed assertion
- **verified:** load-bearing claims are cross-checked against more than one source; contradictions are surfaced, not buried
- **scoped:** the report answers the question it set out to answer — scope stated up front, no drift into adjacent topics
- **recency:** time-sensitive claims carry their date or version, and stale sources are flagged as stale
- **actionable:** closes with a conclusion or recommendation the reader can act on, with the tradeoffs named
