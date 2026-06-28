---
docType: rulebook
testType: unit
rubric: ../../../../../tests/Rubrics/Unit.md
harness: ../../../../../tests/Harness/README.md
---

## Rules

### RunCommand running a successful command → result with exit code 0, captured stdout, and TimedOut false
- **Why:** callers branch on ExitCode and read Stdout to act on output, so a successful run must report success and faithfully surface what the process printed, not lose or corrupt it.

### RunCommand running a command that exits non-zero → result carrying that exact exit code, TimedOut false
- **Why:** the exact exit code is how callers distinguish failure modes; collapsing it to a generic non-zero or mislabeling failure as a timeout would hide why a step actually failed.

### RunCommand whose command outlives the configured timeout → result with TimedOut true
- **Why:** a hung child process must be detectable and reapable rather than blocking forever, so the timeout has to be observable distinctly from an ordinary exit.

### EnsureOk on a non-zero result → throws InvalidOperationException naming the failed step
- **Why:** EnsureOk is the fail-fast guard pipelines rely on, so a failed step must abort loudly with a message that pinpoints which step broke.

### JsonRepository → round-trips entities through Add, Get, Update, and Remove
- **Why:** the storage seam's core contract — an entity written through the repository is read back, replaced,
  and deleted by id, with `GetAll`/`GetById` reflecting each mutation.

### JsonRepository on a fresh instance → reloads persisted entities
- **Why:** writes are durable — a new repository over the same store sees everything a prior instance wrote,
  proving persistence is on disk, not just in the in-memory cache.

### JsonRepository with an unreadable backing file → starts empty
- **Why:** a corrupt or unreadable store is non-fatal — the repository starts empty and a subsequent write
  recovers it, so a bad file never crashes startup.

### JsonRepository under concurrent writers → no torn store
- **Why:** the `SemaphoreSlim` + atomic temp→`File.Replace` write means concurrent `Add`s all land and the
  on-disk file always parses — no torn write under contention.
