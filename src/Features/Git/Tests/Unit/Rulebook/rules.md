---
docType: rulebook
testType: unit
template: ../../../../../../tests/Templates/unit.template.md
harness: ../../../../../../tests/Harness/README.md
---

## Rules

### Force push to a protected branch → refused with InvalidOperationException
- **Why:** a force push to main/master can silently destroy shared history, so the guardrail must hard-fail before the dangerous command ever reaches git.

### Commit with an invalid input (no files or blank message) → refused with ArgumentException
- **Why:** an empty file set or whitespace-only message yields a meaningless or empty commit, so the call must be rejected at the boundary rather than producing junk history.

### Status on a dirty tree → Paths lists each modified and untracked path
- **Why:** callers decide what to stage/commit from this list, so both already-tracked edits and brand-new files must surface or work silently gets dropped.

### Status → IsDirty reports whether the working tree has uncommitted changes
- **Why:** the dirty flag gates whether a commit step runs at all, so a false negative would skip persisting real work and a false positive would commit nothing.

### Commit of listed files → stages and commits them, returning the full hash and subject and leaving the tree clean
- **Why:** downstream steps key off the returned 40-char hash and subject to reference the commit, and a residual dirty tree would prove the stage didn't actually capture the file.

### Diff on a dirty tree → reports the changed-file count and the diff text naming each file
- **Why:** the diff is what gets shown to the model/user for review, so the count and per-file text must reflect the real edits, not a stale or empty snapshot.

### Status after a reverting checkout → reports a clean tree
- **Why:** a revert that still showed phantom changes would trigger needless commits and mislead the dirty check, so state must reset to truly clean.

### Force push to a remote that advanced since the last fetch → refused before it can overwrite
- **Why:** the cascade force-pushes rebased branches; `--force-with-lease --force-if-includes` must reject a stale overwrite so a collaborator's pushed work is never silently lost (a lease alone is defeated by a background fetch — spike research/stacked-prs.md §9).
