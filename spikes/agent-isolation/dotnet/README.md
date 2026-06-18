# .NET re-proof — the return-path harvest, as executable acceptance criteria

The bash spike proves the boundary on Linux primitives. This is the other half of
"the matrix is the portable artifact": the **return-path rows re-run against a .NET
harvest**, in the language the real A.Box harvest will be written in. The behaviours
the fixes depend on are all **non-default** — exactly what silently regresses in a
re-implementation — so they are pinned here as runnable acceptance criteria.

```bash
dotnet run Harvest.cs        # .NET 10 file-based app; prints the matrix, exit 0 = GREEN
```

It is a single file (no `.csproj`, so it never enters `ABox.slnx` or the test
harness): `Harvest` is the primitive under test; `SpikeRunner` stages a throwaway
bare+clone+secret+work tree per row and drives the **real** primitive.

## What it proves (the rows that transfer to .NET)

Only the **return-path** rows port — they are pure harvest logic, OS-independent.
The outward-perimeter rows (A1–A6) are Linux *mechanism* (users, namespaces,
netfilter) and stay in the bash spike; the ConPTY seam is integration work with no
code to test yet.

| # | Attack | Defence pinned |
|---|---|---|
| R1 | a worker `.git/hooks/pre-commit` is ingested / runs on commit | enumeration prunes every `.git` segment; commit forces `core.hooksPath=/dev/null` |
| R2 | a worker symlink reaches the committed tree | `lstat` classify → skip non-regular; strip any symlink before commit |
| R3 | a symlink-to-secret has its **target** copied | `lstat` never follows the link; only `S_IFREG` is ingested |
| R4 | a FIFO in the tree hangs the harvest | `lstat` skips non-regular, so it is never `open()`ed (no blocking read) |
| R6 | an empty diff throws → strands teardown | the commit step is empty-diff tolerant (no throw) |
| A7 | the diff lands authored as the bot | identity lives only in the harvester; author verified from the bare remote |

`IsRegularFile` uses `lstat` (P/Invoke) rather than a bounded read: classifying the
path *without opening it* is the stronger defence — it covers symlinks, FIFOs,
sockets and devices in one check, where the bash spike needed `--no-dereference`
**plus** a `timeout`.

## Why the GREEN is trustworthy

- **Positive control (PC1).** The secret is real and the harvester can read it
  (sha256 + length) — so "no secret copied" means something.
- **Negative control (PC2 / R1).** Each negative row is shown to *fail the naive
  way first*: a naive recursive copy **does** ingest the `.git` hook (R1) and
  **does** follow the symlink and leak the secret (R3). A PASS counts only because
  the vulnerable path demonstrably leaks.
- **Falsifiable.** Force `IsRegularFile` to return `true` (defeat the classify) and
  R3 *and* R4 both flip to FAIL; restore it and they go GREEN. The matrix is a real
  signal, not a false-always-pass.

## Honest scope

- **Return-path only.** A1–A6 (env scrub, secret-at-rest, egress, callback) are OS
  mechanism — see the bash spike and the egress rung. They are not re-proven here.
- **Linux x86-64.** Uses `lstat`/`mkfifo`/`ln -s`. The `Stat` layout is the Linux
  x86-64/arm64 `struct stat`; Windows re-proof (no `.git` ingest, no reparse-point
  follow, Job-Object teardown) is the separate re-proof item.
- **Content is committed by design.** Like the bash spike, the seam blocks
  control-flow injection, not untrusted file *content* — that gate is downstream
  review + CI + ruleset.
- **Not the real harvest yet.** A.Box has no harvest module at this layer; this is
  the acceptance criteria that the real one must keep passing when it is built.
