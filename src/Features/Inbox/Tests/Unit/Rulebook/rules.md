---
docType: rulebook
testType: unit
rubric: ../../../../../../tests/Rubrics/Unit.md
harness: ../../../../../../tests/Harness/README.md
---

## Rules

### Inbox.Get → the item by id, or null when absent
- **Why:** a read is pure — fetching an item must not change it (seen is an explicit, client-driven signal, not
  a side effect of a read), so GET stays safe to cache/retry; a missing id returns null rather than throwing.

### Inbox.MarkSeen → the item stamped seen once and stable on repeat, null when absent
- **Why:** "seen" is reported by the client, the only authority on what a human actually viewed; the surface
  stamps SeenAt on the first mark and a re-mark must not move it, with a missing id returning null.

### Inbox.Complete → the item marked complete once and stable on repeat, null when absent
- **Why:** completion is the terminal interaction stamp the surface drives; the first complete records when the
  item was resolved and a later call must not move it, with a missing id returning null.

### Inbox.Query → items carrying every requested tag (matched case-insensitively) in arrival order, all when no tag given
- **Why:** the inbox is a flat chronological feed with an AND tag filter — no tag returns everything in arrival
  order, a tag set narrows to items carrying all of them, and tags are human labels so the match ignores case —
  so the surface can scope without a priority engine.

### InboxItem persisted through the repository → reloads as its concrete subtype
- **Why:** the inbox holds a polymorphic item hierarchy in the shared JsonRepository, so an item written and read
  back from a fresh repository must round-trip as its concrete subtype (not the abstract base or a wrong type),
  proving the type discriminator survives persistence.
