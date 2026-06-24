# Selector — author a block-structured doc from a dump

> **Wired:** this is the origin of the canonical `create-doc` agent
> (`.claude/agents/create-doc.md`) + `/create-doc` command. Keep them in sync;
> the agent is canonical.

Turn an ephemeral **dump** (a brain-dump or source plan) into a conformant,
block-structured instance under `out/`, gated by the engine. Do NOT invent the
format — read it from the data (catalog + per-block rubrics).

## Inputs
- **The dump material** — from any of: a file path, inline pasted text, or the
  current conversation. It is scratch; the durable artifact is the block file.
- **The target doc type (optional)** — if the caller names one ("make this a
  research"), use it; otherwise infer it from the dump.
- The engine: `blocks/*.yaml`, `doctypes/*.yaml`, and the `docengine` CLI
  (`catalog` / `validate` / `outline`), run with `dotnet run --project . -- <cmd>`.

> Context caveat: "dump from the conversation" only works when you run in the
> session that holds it (a skill / main-loop run). A sub-agent starts fresh, so it
> must be handed the dump as a path or inlined text.

## Procedure
0. **Obtain the dump.** Resolve the source: read the given path, use the pasted
   text, or — when running in a session that already discussed the work — distill
   from the conversation. No file is required.
1. **Choose the doc type.** If the caller named one (e.g. "make this a research"),
   use it. Otherwise run `dotnet run --project . -- catalog` and pick the doc type
   whose `description` fits the dump — the doc-type decision matrix.
2. **Read the doc type.** `doctypes/<docType>.yaml`: its `blocks` (catalog),
   `required` set, `attrs` (front matter), and `rubric`. Follow the rubric.
3. **Pick blocks.** `dotnet run --project . -- catalog <docType>` → choose blocks whose
   `description` matches real content in the dump. Required blocks must appear.
   Include only what carries substance — no filler.
4. **Author each block** to its own `rubric` (`blocks/<type>.yaml`):
   - Singletons → `## <Type>`. Collections → `## <Group>` then `### <title>` members.
   - A stable `<!-- id: N -->` under each header; scalar attrs as `key: value` lines.
   - Distill, do not transcribe. Name real files/symbols from the dump; never invent.
5. **Front matter.** Top of the file, a `---` block: `docType`, `status: draft`,
   `source: <dump path>`.
6. **Gate.** `dotnet run --project . -- validate out/<slug>.plan.md`; fix every
   violation; repeat until it PASSes.
7. **Index.** `dotnet run --project . -- outline out/<slug>.plan.md --write`.
8. **(Optional) grade.** The judge marks each line of the doc-type's `rubric`
   pass/fail; address fails.

## Discipline (mirror the doc rubric)
- The doc stands alone — no "the dump" / chat / revision language.
- One bottom Open Questions group, each with a `lean`.
- Bold marks labels, not inline emphasis.
