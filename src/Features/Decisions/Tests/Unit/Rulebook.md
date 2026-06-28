---
docType: rulebook
testType: unit
rubric: ../../../../../tests/Rubrics/Unit.md
harness: ../../../../../tests/Harness/README.md
---

## Rules

### Decisions.Raise → stores the question and pushes a matching inbox item sharing its id
- **Why:** a raised decision must both persist and surface in the inbox under the same id, so the human sees it
  in the feed and the answer can close both sides through one identifier — the dependency points Decision → Inbox.

### Decisions.Get → the decision by id, or null when absent
- **Why:** a read is pure — fetching a decision must not change it — so it stays safe to retry; a missing id
  returns null rather than throwing.

### Decisions.List → every decision in arrival order
- **Why:** the decision feed is flat and chronological like the inbox, so listing returns all decisions ordered by
  creation (id as a stable tiebreaker), giving the client a deterministic order without a priority engine.

### Decisions.Answer → the decision recorded with its yes/no answer once and stable on repeat, null when absent
- **Why:** answering records the human's yes/no (with an optional note) the first time and a re-answer must not
  move the recorded answer or its timestamp; a missing id returns null rather than minting one.

### Decisions.Answer → completes the inbox item it raised
- **Why:** resolving a decision must complete the inbox item raised under the same id, so the feed reflects the
  decision is handled without the client reconciling two surfaces by hand.
