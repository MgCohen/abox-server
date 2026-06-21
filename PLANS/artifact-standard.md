# Artifact Standard — agent-first types, free in format, enforced at the floor

> **Status: foundation (2026-06-17) — floor LOCKED.** Defines the reusable pattern and
> infrastructure for adding new *artifact types* to this repo. **Deliberately
> independent of the ADR-harness work** — ADRs are one future *instance* of this
> standard, not its basis; nothing here depends on that plan. Supersedes the framing
> in [`test-artifact-migration.md`](test-artifact-migration.md), now marked
> deprecated; the test→artifact migration will be re-derived from this floor.

## Goal

Create a reusable pattern + infrastructure for **new artifact types** that gives each
type **freedom in its format**, while **enforcing that every type carries enough
structure and enforcement to be agent-first.** The reusable thing is not a template —
it is a **meta-standard**: a contract that says *"to be a legitimate artifact type you
must provide these things,"* plus the machinery that enforces that contract over every
registered type. Freedom in the **what** (format); enforcement of the **floor**.

This is what the test `Meta` suite already does for *test types* — it polices that
every test type has a well-formed template with criteria, rules that match it, and
parity with its tests. The standard **generalizes that meta-layer** from "test types"
to "any artifact type."

## What "agent-first" demands — deriving the floor

The bar is not arbitrary; it falls out of asking *what an agent needs in order to
find, produce, and trust an artifact.* Each need pins one required component:

| An agent must be able to… | …so the type must declare | Required? |
|---|---|---|
| **Find** the type and its instances | **registration** — home + profile in the central registry | ✅ required |
| **Select** the right type for a need | a **purpose / when-to-use** line, carried by its registration | ✅ required |
| **Produce** a conforming instance | a **template** — the owned shape it fills (never copy-a-sibling) | ✅ required |
| **Judge quality** beyond mere structure | **criteria** — a rubric the template carries | ✅ required |
| **Verify structure** deterministically | a **validator binding** — the generic structural check, wired to the template | ✅ required |
| Catch **drift** against a second representation | **parity / binding** | ⚪ optional — only types that *have* a second representation (e.g. tests ↔ code) |

## The agent-first floor (the contract)

Every artifact type **must** clear (**locked 2026-06-17**):

> **{ register (home + purpose + profile) · template · criteria · structural validation }**

and **may** add, by its own nature: **parity** (a second-representation binding) and
any **custom checks** or generated outputs. A type cannot be registered below this
floor — that is the whole guarantee. The floor is the load-bearing knob: changing it
re-scopes everything downstream, so tighten or loosen it only by a deliberate decision.

## The reusable infrastructure

Three generic pieces every type gets **for free**, and one that polices the floor:

1. **Registry** — each type declares its profile (home, format/family, which enforcers
   apply). Discovered, not hand-maintained.
2. **Generic structural validator** — parameterized by a type's template schema; checks
   that every instance conforms. One implementation, any type's template.
3. **Generic judge** — reads a type's criteria, grades an instance. One implementation.
4. **The meta-guard** — enforces the **floor itself**: every registered type *has* a
   template, the template *has* criteria, it is *bound* to a validator, its home
   *exists*. This polices the type **definitions**, not just the instances — it is the
   part that guarantees "every artifact type has enough enforcement."

Division of labour: the **type provides** registration + a template (with criteria) +
optionally custom enforcement. The **infrastructure provides** validation, judging, and
the meta-guard. Format is the type's; the floor is the infrastructure's.

## Freedom vs. enforcement, drawn sharply

| Free — the type owns | Enforced — the meta-layer owns |
|---|---|
| the template's sections, front-matter, prose shape | that a template **exists** and is the single owner of the shape |
| the criteria's content | that criteria **exist**, so the type is gradeable |
| which custom checks / parity it adds | that the structural floor is **validated** |
| the wording of its purpose | that it is **registered** with a real home **and a declared purpose** |

A new type can look nothing like a test — and still be guaranteed agent-first, because
it cleared the same floor.

## The keystone — it governs itself

The contract is *itself* an artifact type: the **artifact-type definition**. Its
template is the meta-template ("a type must declare home + template + criteria +
validator"); the meta-guard is its validator. The system is therefore **reflexive** —
the thing that defines artifact types is governed by the same machinery, exactly as
`Meta` is a Rulebook that validates Rulebooks. That reflexivity is what makes this
*infrastructure*, not a pile of conventions: no type, including the meta-type, escapes
the floor.

## Tests are the first instance, not the standard

The test taxonomy is **one instance** of this pattern — the only one fully built today.
The infrastructure is *extracted* from it (registry, generic validator, judge,
meta-guard already exist there in test-specific form). Future types — plans, research,
and yes eventually ADRs — become **new instances** that clear the same floor, each free
in format. None of them is privileged; tests are just where we prove the machinery.

## Scope and independence

- **In scope:** the meta-standard (the floor), the shared infrastructure, and the
  reflexive guarantee.
- **Independent of the ADR-harness effort by intent.** ADRs are a *future instance*;
  this document neither depends on nor prescribes that work. Keeping them separate
  avoids re-merging a specific application into the general pattern prematurely.
- **Orthogonal to the governance relocation** (where things physically live): the
  standard says *what every type must provide*, not *which folder it sits in*.

## Decisions (locked 2026-06-17)

The floor unblocked four design questions; all now locked:

1. **Registry — per-folder + generated index (Q1 = C).** Each type is a folder
   `governance/artifacts/<Type>/` carrying a YAML `artifact.yml` (the profile) +
   `template.md` (shape + `## Criteria`); the harness discovers them; a generated
   `INDEX.md` gives the matrix view without a second source of truth.
2. **Enforcer — generic core + adapters (Q2 = C).** Structural validation is the
   generic core (every instance matches its type's template). **`parity` is the first
   adapter** (code-first), *not* core — promoted to a generic adapter mechanism only on
   a second real case.
3. **Tests — one `Test` artifact (Q3 = A).** The seven test types share one profile, so
   they are **one** artifact with per-type sub-folders; the per-type variation lives
   inside the code-first adapter, not in a generic registry sub-type concept.
4. **Migration — pilot then sweep (Q4 = A).** Re-derive the test→artifact move from this
   floor; pilot one sub-type through the engine repoint, then sweep the rest. See
   [`test-artifact-migration.md`](test-artifact-migration.md).

**Floor: LOCKED 2026-06-17** — { register(home + purpose + profile) · template ·
criteria · structural-validation } required; parity + custom optional.
