# Spike: contract-versioning — feasibility probe

**Question:** can we auto-derive the `ABox.Api` package version on merge to `main` by
detecting *what changed in the shipped wire contract*, with no human picking a bump?

**Answer: yes.** Reflection-only (`MetadataLoadContext`, no code execution, no network),
diff the public surface of the bundled `*.Api` assemblies between two builds. Proven
against real rebuilt assemblies:

| Case | Demo | Detected |
|---|---|---|
| Api dll **added** (new feature) | bundle gained `ABox.Projects.Api` | `AssemblyAdded` |
| Api dll **removed** | bundle lost `ABox.Projects.Api` | `AssemblyRemoved` |
| dll **content changed** (api change) | added `Priority` to `AddNoteRequest` | `SurfaceChanged` (member-level: `+ property`, ctor `(…)`⇒`(…)`) |
| **code behind** dll changed, dll not | edited an impl endpoint body | `None` — `ABox.Inbox.Api.dll` byte-identical |
| nothing changed | rebuilt identical source | `None` (deterministic build) |

**Key truth:** the `.Api` dll *is* the contract boundary. A behavior change that doesn't
reach the dll is invisible to contract-versioning — correctly, because the client's wire
contract genuinely didn't change. Behavioral regressions behind an unchanged contract are
a server-deploy concern, not a package-version one.

**Cost/confidence:** milliseconds over already-built DLLs; high confidence for
add/remove/change at member granularity (it reads the exact surface the client binds to).
A member *rename* shows as remove+add (not "changed"); a *retype* shows as "changed" —
both are breaking, so the bump is unaffected.

## Decided ruleset (pre-1.0, owner-confirmed)

Package is at `0.0.2`; staying on the `0.x` line (not API-stable yet, so major is frozen at 0).

| Detected delta | Bump | Example |
|---|---|---|
| removed / changed member, removed assembly (**breaking**) | **minor** | `0.0.2` → `0.1.0` (patch resets) |
| added member / added assembly (**additive**) | **patch** | `0.0.2` → `0.0.3` |
| binary-only changed / none | **skip** | — |

Precedence: breaking > additive > skip. A merge that both adds and removes → minor.
When the owner declares stability, cut `1.0.0` by hand; from then on breaking → major.

## CLI

```
abox-version-spike dump  <dir>                 # print the *.Api surface of a build output dir
abox-version-spike diff  <beforeDir> <afterDir># classify the delta between two build output dirs
```

## Promotion path

Promote to `tools/abox-version` (out of `ABox.slnx`, like `tools/doc-engine` /
`tools/hooks`): keep the surface diff as the core, add bump computation from the latest
`v*` tag, and drive it from a `version-on-merge.yml` GitHub Action that tags `vX.Y.Z` so
the existing tag-gated `publish-contracts.yml` publishes. Manual tagging stays as the
override; a label/commit-trailer (`[release:skip]` / `[release:minor]`) can force or
suppress.
