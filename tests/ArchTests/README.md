# Architecture tests

Reference-graph enforcement (ArchUnitNET). Fails the build when the dependency rules of the
structure migration are violated, so the clean DAG we reached can't silently regress.

## How it fits together

- **`Rules/rules.md`** is the **single source of truth** — one `###` block per rule, in plain
  language (question / result / why). This is the rule book; read it first.
- **`RuleTests.cs`** has one `[Rule("<block name>")]` test per block — the executable assertion.
- **`RuleParityTest.cs`** fails if a block has no test, a test cites a missing/renamed block, or a
  rule is tested twice. The block and its test can't drift apart.
- **`ArchitectureModel.cs`** loads the production assemblies once and defines the **categories**
  (Contracts, Infrastructure, Domain, Features, Host) by namespace convention. Rules target
  categories, so a new assembly that lands in an existing band is covered with no rule change.

## How to extend

| Want to… | Do this |
|----------|---------|
| Add a rule | append a `###` block to `rules.md`, add a `[Rule("<that name>")]` test in `RuleTests.cs` |
| Add a production assembly / feature / slice | **nothing** — the csproj globs `src\**\RemoteAgents.*.csproj` and `ArchitectureModel` loads them from the output dir, so a new project named `RemoteAgents.*` is discovered and governed automatically (Web is the one deliberate exclude) |
| Add a category / band | add one `IObjectProvider<IType>` in `ArchitectureModel`, plus the rule(s) that constrain it |

The orphan-guard rule (*Every type belongs to a category*) fires if a new assembly lands in a
namespace outside the known bands — the tripwire that reminds you to extend the model rather than
leave the new code ungoverned.

## Not yet enforced (deliberate)

- **`Web → Contracts only`** — `RemoteAgents.Web` isn't loaded into the model yet (Blazor WASM).
  It is the strictest edge and the most drift-prone; wire it in when the UI direction settles.
- **`PtySession` internal to `Domain.Agents`** (the spawn wall) — add once it's internalized.
- **Per-feature `Contracts/` nested in a feature** — a future graduation of *Contracts is a leaf*;
  at that point the cross-feature rule also graduates to exclude peer Contracts (the legal channel).

## A note for future work

The remaining gap is an **agent that audits and authors the actual test behaviour** from each
block — today the `[Rule]` body is hand-written to match its block's prose. Parity keeps them
linked; it does not prove the assertion faithfully encodes the sentence. That review is deferred.
