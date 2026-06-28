---
docType: rulebook
testType: unit
rubric: ../../../../../tests/Rubrics/Unit.md
harness: ../../../../../tests/Harness/README.md
---

## Rules

### Project.Create → a project with a trimmed, non-blank name
- **Why:** the create door is the single home of the name invariant — a project cannot exist nameless;
  surrounding whitespace is trimmed and a blank or whitespace-only name is rejected.

### Project.Create → a project whose required path is stored absolute
- **Why:** a project must point at a directory to be launchable; the create door rejects a blank path and
  normalizes whatever it accepts to an absolute path (existence is checked later, at resolve-time).

### Project.Rename → a renamed project with a trimmed, non-blank name
- **Why:** rename is a mutation door and enforces the same name invariant as Create, leaving the project's
  identity (`Id`) and path unchanged.

### Project.MoveTo → a relocated project with an absolutized path
- **Why:** the only door that changes a project's path enforces the same path invariant as Create, leaving
  the project's identity (`Id`) and name unchanged.

### ProjectRepository.GetByName → the project matched case-insensitively, null when absent
- **Why:** name uniqueness on create and project resolution on flow-launch share one query home — a
  case-insensitive name lookup over the store — so the rule isn't duplicated across the two callers.

### ProjectResolver.Resolve → the project for a known id, else a clear failure
- **Why:** flow-launch is keyed by project id. A known id resolves to its stored Project (name for the run
  label, path for the working dir); an unknown id throws, and a stored path whose directory is gone throws —
  so a launch never starts against a non-existent directory.
