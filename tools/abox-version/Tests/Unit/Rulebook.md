---
docType: rulebook
testType: unit
rubric: ../../../../tests/Rubrics/Unit.md
harness: ../../../../tests/Harness/README.md
---

## Rules

### VersionPolicy maps a pre-1.0 breaking change to the next minor and resets patch
- **Why:** while the package is `0.x` the major is frozen, so a breaking contract change must move the minor
  (`0.0.2`→`0.1.0`) with patch reset — mapping it to patch would hide a build-breaking change behind a digit
  the client reads as safe.

### VersionPolicy maps a pre-1.0 additive change to the next patch
- **Why:** pre-1.0 an addition is the safe bump and must land in patch (`0.0.2`→`0.0.3`), so the client can tell
  a pure addition from a break by which digit moved.

### VersionPolicy maps a post-1.0 breaking change to major and a post-1.0 additive change to minor
- **Why:** once `1.0.0` is cut the same classification must shift up one notch (breaking→major, additive→minor)
  with no rule change, or the contract's stability promise silently means the wrong thing.

### VersionPolicy returns no bump for an unchanged contract
- **Why:** `None` must yield no version so publish is skipped — republishing a byte-identical contract is pure
  noise the client should never be asked to take.

### SurfaceDiff classifies a removed assembly or removed or changed member as Breaking
- **Why:** a consumer's build breaks when a shipped DTO disappears or its shape changes, so any removal or
  signature change must classify Breaking regardless of what else is in the same delta.

### SurfaceDiff classifies a pure addition as Additive
- **Why:** a new assembly or new member never breaks an existing consumer, so with no removals or changes present
  the delta is Additive — the safe bump, not a major.

### SurfaceDiff ranks a breaking change above a concurrent addition
- **Why:** a single merge that both adds and removes is still breaking for the client, so precedence must be
  Breaking > Additive > None — never averaged, never dependent on iteration order.

### SurfaceDiff treats a byte-changed assembly with an identical surface as no contract change
- **Why:** a recompile that changes the DLL bytes but not the public surface is not a contract change — the
  `.Api` dll is the boundary — so it must classify None and never trigger a version bump.

### CatalogSignal reports a byte-different or newly-present catalog as changed and an identical or absent pair as unchanged
- **Why:** the shared doc catalog is shipped vocabulary but rides the package as an embedded resource, invisible
  to the surface diff — comparing its bytes is the only signal that its blocks/doctypes moved, so a real change
  must read as changed and a no-op build must not, or a catalog edit silently never publishes.

### CatalogSignal lifts a None surface to Additive when the catalog changed and never downgrades a real surface change
- **Why:** a catalog change adds to what a client can render, so on its own it must trigger an additive (patch)
  publish; but it must only lift `None` — a concurrent breaking or additive surface change already dominates and
  the catalog signal must never soften it.

### SemVer parses a v-prefixed tag and ignores any prerelease or build suffix
- **Why:** release tags carry a `v` prefix and MinVer can append a prerelease/height or build suffix, so parsing
  must read the `X.Y.Z` core (`v0.0.2`, `v1.4.2-alpha.0.5`) or the baseline the bump is computed from is wrong.

### SemVer rejects a non-semver string with an actionable error
- **Why:** a malformed `--current` must fail loudly naming the bad value, not silently yield a bogus next
  version that then gets tagged and published.
