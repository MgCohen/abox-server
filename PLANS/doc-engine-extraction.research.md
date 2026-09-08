---
docType: research
status: draft
---

## Summary

Extracting the doc-engine is tractable and mostly already done: the engine is a
standalone CLI with zero project references, every seam to the rest of the repo is a
process boundary or a data file, and the catalog location is already parameterized
(`--root`). The real structure is **three enforcement pillars** — the doc-engine
(structural), the Rulebook test-harness (cross-artifact), and the judge (semantic) —
that share one model but not one binary. Document enforceability travels with the
doc-engine (validate/check are in the CLI; the semantic `rubric` + judge are portable
data); only test-code enforcement (ParityGuard) stays behind. A full split into
soft-wired packages is sound and the seams already support it, but the repo's own
YAGNI rule argues for phasing the packaging rather than doing it before a second
consumer exists.

## Questions

### What is the doc-engine, and how coupled is it to the rest of the repo?
Extraction cost is set by the coupling. Establish what physically moves and what ties
must be cut in each direction (engine → repo, repo → engine).

### What actually enforces that a document is "good", and does that enforcement travel?
The owner's goal is to keep the engine enforceable after extraction. Locate every
mechanism that decides a document is well-formed and well-authored, and check whether
each moves with the engine or is left behind.

### How do the three enforcement systems relate — is the "loop" a real dependency cycle?
The engine validates the Rulebooks that describe the tests that guard the engine. Decide
whether that circularity blocks extraction or is harmless self-hosting.

### Can the tree become semi-independent packages with optional soft dependencies?
The target is: publish the tree, install only what you need, and have the packages use
each other automatically when present. Determine whether the current seams support that.

## Expected Result

The engine was expected to be cleanly separable (it was deliberately kept out of
`ABox.slnx`), but the enforcement was expected to be deeply entangled with the xUnit
test harness — such that "keeping it enforceable" would require lifting a large,
generic test engine along with it.

## Quotations

### Engine is deliberately standalone
source: tools/doc-engine/README.md
"A standalone .NET tool (`ABox.DocEngine`, the `docengine` CLI) — deliberately NOT in
`ABox.slnx`: it is dev tooling, not the orchestrator spine, so it carries its own
YamlDotNet dependency without touching the product's zero-dep assemblies."

### Share the data, not the engine
source: tools/doc-engine/SHARING.md
"Rule of thumb: share the data, not the engine. The client never takes
`ABox.DocEngine.dll`. It takes one data file describing the vocabulary and writes its
own renderers against it."

### The two engines are unified in model, split in engine
source: design/adr/0015-rulebook-as-document.md
"We will unify the model, not the engine, and split responsibility by whether a
guarantee is intra-document or spans artifacts." A merge would "make the protected,
zero-dependency enforcement spine depend on the doc-engine and its YamlDotNet,
inverting the dependency arrow the control surface relies on."

### The judge is topic-blind and was deliberately de-packaged from C#
source: PLANS/generic-judge.md
"No C# now. The C# `Judging/` types had zero consumers (not in the feature map, the
PRD, or any flow) and duplicated the JS. Deleted; rebuilt at the first real ABox
consumer." The judge is "topic-blind. The methodology is ingrained in the agent;
everything topical is input."

### The repo already ruled on packaging timing
source: PLANS/test-harness-extraction.md
"Stay copy-fork for now. Per this repo's YAGNI rule, only after a second consumer is
really using it would extracting the pure engine into a shared `Abox.TestKit` NuGet pay
off … Packaging before that second use is the speculative abstraction the codebase
warns against."

## Analysis

**Coupling is low and one-directional at the code level.** `ABox.DocEngine.csproj`
declares a single `PackageReference` (YamlDotNet) and no `ProjectReference`. It has no
`<TargetFramework>` of its own — it inherits net10.0, nullable, and warnings-as-errors
from the root `Directory.Build.props`, so an extracted repo must reproduce those build
settings plus a `global.json`. `Program.cs` resolves the catalog via `--root` or by
walking up for `_schema/kind.schema.yaml`, and `validate <file>` accepts any path — so
the engine binary and the catalog data are already separable at runtime.

**The seams the repo depends on are all process- or data-shaped, never DLL references.**
`tests/Central/Docs/Support/DocEngine.cs` shells out to the CLI (ADR 0015: "a test type
MAY run the doc-engine, but the Harness never depends on it"). `src/Api/doc-catalog.json`
is a generated export embedded into `ABox.Api` for the client; `tools/abox-version`
diffs that JSON for version signals. `.claude/agents/create-doc.md`, the
`on-doc-change.hook`, and `governance/protected-paths` reference the engine by path.
Every one of these is repointed by changing a path or a tool name, not by breaking a
compile-time edge.

**Enforcement is three pillars sharing one model (ADR 0015), not one system.** The
doc-engine enforces *intra-document* structure — `check` (catalog self-consistency) and
`validate` (an instance conforms to its doctype). Both live inside the CLI, so they
travel automatically; this is the bulk of "is a document good". The **judge** enforces
*semantic* quality — it grades each doctype's `rubric` (binary one-liners) pass/fail.
Per `generic-judge.md` it is topic-blind and dependency-free, living as `.claude/`
agent + workflow files; it lifts out as data. The **Rulebook test-harness**
(`ParityGuard`) enforces *cross-artifact* structure — that `[Rule]` attributes on
compiled tests match the `### ` headers in `Rulebook.md`. That is enforcement of *tests*,
not documents, and it is the only pillar that does not travel.

**The "loop" is self-hosting, not a build cycle.** At the project level the graph is a
DAG: the doc-engine has zero references; the harness string-parses `### ` and also has
zero references; the Docs test references the harness and *shells out* to the prebuilt
engine. The circularity exists only in meaning — the engine's catalog defines the
`rulebook` doctype, which is the shape of the Rulebooks that describe the tests that
guard the engine. This is a compiler compiling itself. It cannot break on extraction
because it stays wherever the `rulebook` doctype and the harness live, which is this
repo; the extracted engine simply guards its own docs with an ordinary test instead.

**The soft-dependency package design falls out of those seams.** Because every tie is a
CLI or a data file, each pillar can be an independently installed tool that detects its
siblings at runtime: the harness runs its `docengine`-validates-Rulebooks check *only if*
`docengine` is resolvable, and `create-doc`/`/judge` invoke the judge *only if* the
`.claude/` bundle is present. In .NET terms: each pillar is `PackAsTool`; a
`.config/dotnet-tools.json` manifest is the "tree you track"; `dotnet tool restore`
installs the chosen subset; and "use each other automatically if there" is a runtime
probe, not a project reference. The Slice Package Topologies study reached the same
conclusion in a different codebase — splitting core from optional layers "costs the user
nothing" and is the only thing preserving reach, while merging forecloses it. The
identical rule holds here: never merge two pillars into one package, or the
"install only what you need" property is lost.

## Outcome

**All four questions resolve in favor of extraction, and the result upset the
expectation.** Enforceability was expected to be trapped in a large test engine; in fact
the document-shape and rubric gates are already inside the CLI and the judge, and only
the test-parity pillar — which enforces tests, not documents — stays behind. So an
extracted doc-engine keeps both grades of *document* enforcement with a ~30-line
plain-xUnit guard, losing only the self-hosting elegance of "its tests are documents it
validates". The loop is harmless self-hosting, not a dependency cycle, so it does not
block anything. The tree can become soft-wired packages because every seam is already a
process boundary or a data file. Recommended phasing: (1) prove the two-pillar carry in
isolation; (2) package the judge first — most portable, dependency-free, soft-depended
on by both others; (3) package the doc-engine as a dotnet tool when the extracted repo
becomes its home; (4) package the test-harness at the abox-client cutover, its already-
blessed second consumer, with the doc-engine as its optional dependency.

## Open Questions

### Package the tree now, or copy-fork until a second consumer exists?
lean: Phase it — copy-fork/prove now, package the judge first, defer engine/harness packages to real second-consumer use.
`test-harness-extraction.md` already ruled "stay copy-fork" under the repo's YAGNI rule,
and the client consumes the catalog *data*, not the engine — so there is arguably still
no second engine consumer. Full packaging now is justified only if cross-repo tool reuse
is an explicit goal rather than a possibility.

### Where do `_schema/` and `kinds/` live once the engine is extracted?
lean: `_schema/` travels with the engine as its generic floor; `kinds/` travels too; `blocks/`+`doctypes/` stay as this repo's vocabulary.
The engine "names no kind", so the meta-layer is generic and the vocabulary is
repo-specific — but `kinds/` (block, doctype) sits on the boundary and could reasonably
ship as engine defaults or stay as repo data.

### Should the judge ever return to being a compiled package?
lean: No — keep it as the `.claude/` bundle until a real in-process C# consumer exists, exactly as generic-judge.md decided.
A C# judge was built and deleted for having zero consumers; re-packaging it as C# repeats
that mistake unless an ABox flow actually needs an in-process verdict.
