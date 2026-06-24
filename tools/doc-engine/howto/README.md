# doc-engine HOWTOs

Step-by-step guides for extending the engine. Each is self-contained.

The stack, top to bottom — every layer is data, enforced by the layer above:

```
_schema/kind.schema.yaml   the meta-schema (one kind, conforms to itself)
        ▲
kinds/*.yaml               a KIND — a category of definition (block, doctype, …)
        ▲
blocks/*.yaml              a BLOCK — a reusable content unit
doctypes/*.yaml            a DOCTYPE — an ordered catalog of blocks
        ▲
out/*.md                   an INSTANCE — an actual document
```

- **[add-a-block.md](add-a-block.md)** — a new reusable content unit.
- **[add-an-instance.md](add-an-instance.md)** — a new document conforming to a doc type.
- **[add-a-kind.md](add-a-kind.md)** — a new *category* of definition (advanced; the meta level).

Verify after any change, from `tools/doc-engine`:

```bash
dotnet run --project . -- check                              # definitions conform
dotnet run --project . -- validate out/<file>               # an instance conforms
```
