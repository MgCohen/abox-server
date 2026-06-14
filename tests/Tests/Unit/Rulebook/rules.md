# Unit Rulebook

Each Rule is one behavioral guarantee about a single type or slice tested with local fakes. Each `###`
header IS the Rule, stated as the guarantee itself; the bullets carry its rationale. A `[Rule("<header>")]`
test in `Unit/Tests/` enforces it, and `ParityTests` fails the build if a Rule has no test or a test cites
no Rule. Cardinality is **1:N** — one guarantee may be realized by several case tests (several
`[Rule("<same header>")]` methods, or a future theory-backed Rule).

Template:
```markdown
### <Subject> <condition> → <expected result>
- Why: <the contract this protects>
```

> **Adoption is going-forward.** This Rulebook starts empty and grows as new behavioral tests land with
> their Rule. Existing `[Fact]` tests are backfilled opportunistically, never in one swept pass — so the
> set of Rules below is intentionally smaller than the set of tests in `Unit/Tests/`.

---

### Project.Create mints a project with a trimmed, non-blank name
- Why: the create door is the single home of the name invariant — a project cannot exist nameless;
  surrounding whitespace is trimmed and a blank or whitespace-only name is rejected.

### Project.Create requires a path and stores it absolute
- Why: a project must point at a directory to be launchable; the create door rejects a blank path and
  normalizes whatever it accepts to an absolute path (existence is checked later, at resolve-time).

### Project.Rename returns a renamed project with a trimmed, non-blank name
- Why: rename is a mutation door and enforces the same name invariant as Create, leaving the project's
  identity (`Id`) and path unchanged.

### Project.MoveTo returns a relocated project with an absolutized path
- Why: the only door that changes a project's path enforces the same path invariant as Create, leaving
  the project's identity (`Id`) and name unchanged.

### ProjectRepository.GetByName finds a project case-insensitively, null when absent
- Why: name uniqueness on create and project resolution on flow-launch share one query home — a
  case-insensitive name lookup over the store — so the rule isn't duplicated across the two callers.

### JsonRepository round-trips entities through Add, Get, Update, and Remove
- Why: the storage seam's core contract — an entity written through the repository is read back, replaced,
  and deleted by id, with `GetAll`/`GetById` reflecting each mutation.

### JsonRepository reloads persisted entities from a fresh instance
- Why: writes are durable — a new repository over the same store sees everything a prior instance wrote,
  proving persistence is on disk, not just in the in-memory cache.

### JsonRepository starts empty when the backing file is unreadable
- Why: a corrupt or unreadable store is non-fatal — the repository starts empty and a subsequent write
  recovers it, so a bad file never crashes startup.

### JsonRepository serializes concurrent writers without tearing the store
- Why: the `SemaphoreSlim` + atomic temp→`File.Replace` write means concurrent `Add`s all land and the
  on-disk file always parses — no torn write under contention.
