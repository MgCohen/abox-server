# Selector — author a block-structured doc from a dump

Turn an ephemeral **dump** (a brain-dump or source plan) into a conformant,
block-structured instance under `out/`, gated by the engine. Do NOT invent the
format — read it from the data (catalog + per-block rubrics).

## Inputs
- **The dump material** — from any of: a file path, inline pasted text, or the
  current conversation. It is scratch; the durable artifact is the block file.
- **The target doc type (optional)** — if the caller names one ("make this a
  research"), use it; otherwise infer it from the dump.
- The engine: `blocks/*.yaml`, `doctypes/*.yaml`, `catalog.py`, `validate.py`, `outline.py`.

> Context caveat: "dump from the conversation" only works when you run in the
> session that holds it (a skill / main-loop run). A sub-agent starts fresh, so it
> must be handed the dump as a path or inlined text.

## Procedure
0. **Obtain the dump.** Resolve the source: read the given path, use the pasted
   text, or — when running in a session that already discussed the work — distill
   from the conversation. No file is required.
1. **Choose the doc type.** If the caller named one (e.g. "make this a research"),
   use it. Otherwise run `python3 catalog.py` and pick the doc type whose
   `description` fits the dump — the doc-type decision matrix.
2. **Read the doc type.** `doctypes/<docType>.yaml`: its `blocks` (catalog),
   `required` set, `attrs` (front matter), and `rubric`. Follow the rubric.
3. **Pick blocks.** `python3 catalog.py <docType>` → choose blocks whose
   `description` matches real content in the dump. Required blocks must appear.
   Include only what carries substance — no filler.
4. **Author each block** to its own `rubric` (`blocks/<type>.yaml`):
   - Singletons → `## <Type>`. Collections → `## <Group>` then `### <title>` members.
   - A stable `<!-- id: N -->` under each header; scalar attrs as `key: value` lines.
   - Distill, do not transcribe. Name real files/symbols from the dump; never invent.
5. **Front matter.** Top of the file, a `---` block: `docType`, `status: draft`,
   `source: <dump path>`.
6. **Gate.** `python3 validate.py out/<slug>.plan.md`; fix every violation; repeat
   until it PASSes.
7. **Index.** `python3 outline.py out/<slug>.plan.md --write`.
8. **(Optional) grade.** The judge (`criteria/<docType>.yaml`) checks selection +
   quality; address fails.

## Discipline (mirror the doc rubric)
- The doc stands alone — no "the dump" / chat / revision language.
- One bottom Open Questions group, each with a `lean`.
- Bold marks labels, not inline emphasis.
