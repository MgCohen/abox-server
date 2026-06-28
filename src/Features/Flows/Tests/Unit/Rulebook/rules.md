---
docType: rulebook
testType: unit
rubric: ../../../../../../tests/Rubrics/Unit.md
harness: ../../../../../../tests/Harness/README.md
---

## Rules

### Flow that runs every operation successfully → Completed with each operation recorded as Completed
- **Why:** the orchestrator's whole job is to drive a flow to a terminal success; if the phase or per-operation status drifted, the UI would show a half-finished or stuck run for work that actually succeeded.

### Snapshot version after a run → strictly greater than before the run
- **Why:** clients diff on the version to detect new state; a non-monotonic version would let a stale snapshot masquerade as current and silently drop updates.

### Flow whose operation throws → Failed with the operation Failed and its error recorded
- **Why:** failures must surface as a Failed phase with the actual error text, otherwise a broken run looks healthy and the operator never sees why it died.

### Flow that passes context data into an operation's args → that data appears in the operation's recorded result
- **Why:** flows are useless if request/context data can't reach the operation that consumes it; this proves the data plumbing from context through args is wired.

### Flow that runs operations concurrently → every operation recorded exactly once with no loss or corruption
- **Why:** the snapshot recorder is shared mutable state, so concurrent operations could race and lose or duplicate entries; this guards the thread-safety of the run record.

### Changes subscribed after a run finished → yields the latest snapshot once then completes
- **Why:** a late subscriber must still get the final state and a clean stream completion, otherwise it would hang waiting or miss the result entirely.

### FlowDefinition built from a type that is not a Flow → throws ArgumentException
- **Why:** catching a non-Flow type at definition time turns a wiring mistake into a loud construction error instead of a runtime cast failure deep in execution.

### FlowDefinition built from a concrete Flow type → exposes that type as its FlowType
- **Why:** composition relies on FlowType to instantiate the right flow; if it didn't round-trip the supplied type, the registry would build the wrong flow.
