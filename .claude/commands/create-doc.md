---
description: Create a structured block document (plan/research/proposal/…) from the conversation, a file, or pasted text — doc type named or inferred.
---

You are the **/create-doc** adapter. Arguments: $ARGUMENTS

Resolve two optional inputs from $ARGUMENTS:

1. **Doc type** — if the first token names a known doc type, use it. (List them
   with `cd spikes/doc-engine && python3 catalog.py`.) Otherwise the type is
   **inferred** from the dump.
2. **Dump source** — a remaining file path or quoted text is the dump. If none is
   given, the dump is **this conversation**: synthesize it yourself from the
   context. (Do this inline — a sub-agent would not have the conversation.)

Then apply the **create-doc** methodology — read `.claude/agents/create-doc.md`
and follow its procedure against the engine in `spikes/doc-engine/`: choose the doc
type, author blocks from the catalog, write the instance with a `---` front-matter
block, run `validate.py` until PASS, then `outline.py --write`, then grade with the
judge.

Report: the doc type (named or inferred), the blocks chosen (one line each), the
final `validate.py` result, and anything from the dump you deliberately dropped.

When the dump is a file or pasted text (not the conversation), you may delegate to
the `create-doc` agent; for a conversation dump, do it inline so the context is
available. If $ARGUMENTS names a doc type that does not exist yet, say so and list
the available types instead of inventing one.
