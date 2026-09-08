# spike: judge extraction — the portable pillar (Phase 2)

Proves the **judge** — the semantic/soft enforcement pillar — is a drop-in
`.claude/` bundle with a portable, host-agnostic core. It is packaged first
because it depends on nothing and both other pillars soft-depend on it.

See [`PLANS/doc-engine-extraction.research.md`](../../PLANS/doc-engine-extraction.research.md)
for the full analysis; this is its Phase 2 executable proof.

## What's here

| Path | What it is |
|---|---|
| `bundle/.claude/` | The judge as it would land in a new repo — a verbatim copy of the agent, workflow, command adapters, and the test-rulebook skill. |
| `MANIFEST.md` | The transfer set, the core-vs-adapter split, and the contract everything soft-depends on. |
| `verify.sh` | The proof: core lifts with zero host coupling, contract intact, adapters isolated as the seam. |

## What it proves

```
bash verify.sh   # PASS — judge core is portable verbatim; adapters isolated as the seam
```

1. **The core lifts verbatim** — `agents/judge.md` + `workflows/judge.js` carry
   none of this repo's tokens (`ABox`, `abox-server`, `tests/Central`, the
   marker). The judge is topic-blind by construction, and the scan proves it.
2. **The contract survives** — `judge.js` parses and still exports the full
   request/response schema every adapter conforms to.
3. **The coupling is quarantined in adapters** — only the `test-rulebook` skill
   carries host tokens (the ABox test taxonomy). That is the per-host
   re-authoring surface, exactly where coupling belongs.

## Isolation

Not in `ABox.slnx` or `dirs.proj`, so CI neither builds nor runs it. The copied
`.md` files carry `name:`/`description:` frontmatter, never a leading `docType:`,
so the repo's own `Docs` test does not discover them as document instances.
Run by hand to validate the seam; delete once the real extraction lands.
