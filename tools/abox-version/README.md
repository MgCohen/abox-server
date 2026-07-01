# abox-version

Computes the next `ABox.Api` package version from **what changed in the shipped wire
contract** — no human picks the bump. Standalone CLI, deliberately out of `ABox.slnx`
like `tools/doc-engine` and `tools/hooks`.

It diffs the **public surface** of the bundled `*.Api` assemblies (the DTOs the client
consumes — `*.Contract` leaves never ship) between two builds, classifies the delta, and
maps it to a SemVer bump. Reflection-only via `MetadataLoadContext`: the target code never
runs, no network.

## The contract boundary

The `.Api` dll *is* the contract. A change behind it that doesn't reach the dll (impl,
behavior) is invisible here **by design** — the client's wire contract genuinely didn't
change, so the package shouldn't bump. Behavioral regressions behind an unchanged contract
are a server-deploy concern, not a package-version one.

## Bump rules

One compatibility classification, correct on both sides of 1.0:

| Detected delta | Classification | Pre-1.0 (`0.x`) | Post-1.0 |
|---|---|---|---|
| removed assembly, removed member, changed member | **Breaking** | minor (`0.0.2`→`0.1.0`) | major |
| added assembly, added member | **Additive** | patch (`0.0.2`→`0.0.3`) | minor |
| binary-only / nothing | **None** | skip | skip |

Precedence: Breaking > Additive > None. The package is on the `0.x` line (not API-stable);
the owner cuts `1.0.0` by hand, after which the same classifier auto-shifts up one notch.

> Note: adding a property to a *positional record* changes its primary constructor, so it
> classifies as **Breaking** (existing `new T(...)` callers break) — not additive. A new
> standalone type, or a non-ctor `init` member, is additive.

## CLI

```
abox-version next --before <dir> --after <dir> --current <vX.Y.Z> [--catalog-before <file>] [--catalog-after <file>]
  Classify the delta and print the next version, or skip if the contract is unchanged.
  Emits machine-readable `publish=` / `version=` / `level=` (also appended to $GITHUB_OUTPUT).
  The catalog rides the package as an embedded resource, invisible to the surface diff, so pass the two
  doc-catalog.json paths: a byte change counts as an additive contract change on its own (never Breaking).

abox-version diff --before <dir> --after <dir>     # the classified delta, no version math
abox-version dump <dir>                            # the *.Api public surface of one build dir
```

`<dir>` is a build output folder containing the bundled `*.Api.dll` (e.g.
`dotnet build src/Api/ABox.Api.csproj -c Release -o <dir>`).

## How CI uses it (planned)

A `version-on-merge` workflow (owner-gated — `.github/**` is a critical protected path)
runs on push to `main`: resolve the last `v*` tag, build `ABox.Api` at that tag and at
`HEAD`, run `abox-version next` (passing both the baseline and HEAD `doc-catalog.json` so a
catalog-only change still bumps), and on `publish=true` push the computed `vX.Y.Z` tag so
the existing tag-gated `publish-contracts.yml` publishes. No contract change → no tag → no
publish. Manual tagging stays as the override.
