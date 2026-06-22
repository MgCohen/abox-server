# Artifact registry — the agent-first types this repo governs

Each `governance/registry/<Name>/` folder declares one **agent-first artifact type** — a kind of thing an
agent produces that the repo governs. The registry is the single source of *which* types exist; an agent
reads it to tell what it can produce and when to reach for each.

Members today:

| Artifact | Family | Gate | What it is |
|---|---|---|---|
| [`Test`](Test) | code-first | block | A behavior or invariant locked by an executable check (sub-typed: Arch, Unit, …). |
| [`Research`](Research) | nl-first | advise | A multi-source, adversarially-verified cited report that grounds a decision. |

## The floor every artifact declares

Each member carries an `artifact.yml` of flat `key: value` lines. The Meta guard *Every artifact declares the
floor* ([`tests/Meta/Tests/ArtifactTests.cs`](../../tests/Meta/Tests/ArtifactTests.cs)) reads it from disk and
fails the build if any field is missing or inconsistent:

| Field | Meaning |
|---|---|
| `purpose` | when to reach for it — the selection signal an agent reads |
| `home` | where the instances live (must be an existing directory) |
| `family` | `code-first` (binds a second representation) or `nl-first` (a natural-language deliverable) |
| `gate` | `block` (fails a build) or `advise` (informs, never gates) |
| `parity` | the binding target a `code-first` artifact enforces against — **required** for code-first, **forbidden** for nl-first |

## Adding an artifact

Drop a `governance/registry/<Name>/artifact.yml` declaring the floor; the guard covers it the moment the
folder lands. A `code-first` type also needs its parity binding and (like Test) may sub-type into Rulebooks
with `template.md` + `rules.md` under `<Name>/<Type>/`. An `nl-first` type carries no Rules or parity — an
optional `template.md` with `## Purpose` + `## Criteria` gives `/judge` a rubric to grade instances against.
