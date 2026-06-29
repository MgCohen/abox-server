# Spike — North Star (Recipe → Code)

> Branch `claude/csharp-snippet-merge-decl`, stacked on the building-style PR (#109).
> The spike validated the **mechanism** (recipe-based composition, merging) bottom-up.
> This doc fixes the **north star** top-down: the end-result product, our slice of it, and
> the backward decomposition toward it. It supersedes the bottom-up framing in
> `DECLARATION-TIER.md` (which becomes the M1 detail).

## The whole system (context, not our scope)

```
User Intent ─(LLM⇄Human)→ Plan ─→ Tasks/Phases ─(LLM matches)→ Recipes ─(deterministic compose)→ Feature
   human          blue        blue      blue          blue          YELLOW           YELLOW
```

The color is the thesis: **the LLM never writes code.** It produces judgment — a plan, a task
breakdown, and a *selection* (this task → that recipe, with these params). Every line of the
Feature comes out of the **deterministic (yellow)** layer. That is A.Box's "maximum guidance"
made literal: an agent can only compose what the catalog allows — illegal code is
*unrepresentable*, not merely discouraged.

| Lane | Who | Does | Owner |
|------|-----|------|-------|
| Intent → Plan | LLM + Human | plan, clarify, **approve** | another agent |
| Plan → Tasks | LLM | break down, review ordering/roles | another agent |
| Task → Recipe | LLM | **match + parameterize** | another agent (binds our contract) |
| **Recipe → Feature** | Deterministic | **the recipes and their composition** | **us** |

## Our scope

Two things, in order:

1. **The final shape of a recipe.**
2. **Recipe → code.**

Everything left of "Recipe" is someone else's. The seam to them is a **contract**: a recipe's
*name + description + parameter schema*. We define that contract; they bind it.

## Where we sit (the references as a map)

| | Backstage scaffolder | Metalama | **Us** |
|---|---|---|---|
| Unit | a repo/component template | an aspect over existing code | a **component recipe**, composed into a feature |
| Mechanism | string templating (nunjucks) | Roslyn aspects (wrap/augment) | Roslyn lowering of a **typed recipe tree** |
| Output | one-shot boilerplate you then own | compile-time overlay, **not owned source** | **owned source files** |
| Type-safe? | no | yes | yes |
| Composable / re-run? | no | aspects stack | **recipes compose + cross-reference** |
| Coverage | initial setup only | augmentation only | **all major components + glue** |

**Backstage scaffolds once and walks away; Metalama wraps code it doesn't own; we lower a typed
tree into owned source we can compose and re-compose.** Deeper than both = a recipe for *every*
major component plus a thin layer of connecting glue — not just initial setup, not just
augmentation.

## Code shapes — what we build

A containment hierarchy. The recipe's unit is one **Component**; a **Feature** is components
composed + glue. Granularity is **flexible**: a single recipe may cover a whole feature when the
pattern is standard enough (a repository is the canonical example) — the unit is "a coherent,
reusable component," not a fixed altitude.

| Tier | Shape | Recipe? | Spike today |
|------|-------|---------|-------------|
| **Feature** | vertical slice | a *composition* of component recipes + glue | ❌ |
| **Component** | a deployable unit (1+ files) — Model, Repository, Service, API, DI-reg | **one recipe each** (the unit) | ❌ |
| Member | method / field / ctor / property | recipe fragment / fill | partial |
| Stmt / Expr | body | fill | ✅ proven |

For the Spotify "Favorite Artist" slice, the component set and the **output mode** each needs:

| Component | Shape | Output mode |
|---|---|---|
| Model / Entity | record / class | **emit-new** |
| Repository | interface + impl | emit-new |
| Service | interface + impl | emit-new |
| API / Endpoint | MediatR handler + controller (or SignalR hub) | emit-new |
| Docs | markdown | emit-new |
| DI registration | `services.AddScoped<…>()` | **merge-into-existing** |
| Manifest / dependency | csproj / module manifest | **merge-into-existing** |

Two output modes; the second is the hard one:
- **emit-new** — a fresh owned file. The spike already does this. **First.**
- **merge-into-existing** — splice into a *human-owned* file (DI root, csproj). The risky one
  for the deterministic / byte-identical guarantee. **After emit-new; kept in mind from now.**

## The recipe — final shape

**A recipe is any node.** The whole typed tree, from a primitive (`RecordNode`, `LoopNode`, `Var`,
`Lit`) up to a composite that builds a subtree (a future `ScaffoldService`). They differ by *level*,
not kind. A node is **already a parameterized recipe** — `new RecordNode("FavoriteArtist", fields)`
takes its name and fields as args; there is no separate "recipe class" wrapper. Composition is
native: `Block` holds `Stmt[]`, a type holds `Field[]` — the tree *is* recipes composing recipes,
with the slots **typed** so illegal nesting can't compile.

What's *not* intrinsic to a recipe, and lands later (tracked in the backlog, `README.md` §8 #12–14):

| Facet | What it is | Lands at |
|---|---|---|
| catalog name / description / param **schema** | the matcher's contract — lets an LLM select + parameterize | **M3** (when a consumer needs uniform handling; an `IRecipe` marker may join here) |
| **composite** `Build()` — expand to a subtree | only when a recipe needs *more* than one node (a service: class + repo + DI) | when a multi-node recipe forces it (`ScaffoldService`) |
| **inheritance** — a generated type declaring a base (`record User : Model`) | `Model` becomes the base others inherit; needs emitter base-list support | the **inheritance pass** (before/with M4) |

| Facet | What it is | Spike today |
|---|---|---|
| params (typed) | a node's ctor args — `entity`, `fields`, `deps` | ✅ nodes take params |
| target | which component shape it emits | record/class/struct/enum ✅ |
| slots | where custom logic drops in | `Block.Of("id")` ✅ |
| output mode | emit-new vs merge-into-existing | emit-new only |

The spike's three fill forms **already are** the parameter surface — a good sign we're on the path:

| Fill form | The recipe's… |
|---|---|
| param (`Expr<T>`) | scalar config (a name, a count) |
| marker (`Var<T>`) | a handle (the entity, an injected dependency) |
| **block** | **the "custom code waved around the recipes"** — the business-logic slot |

So *"a bit of custom code waved around to connect"* = **block fills**, scaled from "loop body" to
"the service method body." Already first-class.

## Backward decomposition — the climb

What the Feature demands vs. what the spike has proven:

| The Feature needs… | Proven? | Milestone |
|---|---|---|
| Recipe emits a **whole file** (class/entity), not a body in a toy shell | ✅ | **M1 — declaration / file tier** (the `DECLARATION-TIER.md` work) |
| Recipes are **parameterized** (`entity = "FavoriteArtist"`) | ✅ | **free with the node model (M1)** — a node *is* a parameterized recipe; no wrapper layer |
| Generated types **inherit a base** (a `Model` base class) | ❌ | the **inheritance pass** (before/with M4) |
| **Catalog metadata** (name + description + param schema) for the matcher | ❌ | **M3 — catalog surface** (the `IRecipe` marker + metadata land here, when a consumer needs them) |
| Recipe declares its **output target** (namespace + folder) | ❌ | **M4** — couples to cross-references (a using needs a namespace) |
| **Several recipes → one Feature**, cross-referenced (service names the model's type) | partial — merge proven on one tree | **M4 — multi-file composition + cross-references** |
| Real types / method calls (`Task<T>`, the repo dependency) | ❌ int-only | folds into M1/M4 (retire int-only + inline-only) |
| Tasks that **edit existing files** (DI, csproj) | ❌ | **M5 — merge-into-existing** (kept in mind, built last) |
| Deterministic, type-safe, owned files | ✅ | — |

> Deferred items behind these milestones — output target (namespace + folder), inheritance/`Model`
> base, catalog surface, `using`-derivation, the `TypeRef` validator — are tracked in the **canonical
> backlog**, `README.md` §8 (#12–19). This doc keeps only the roadmap.

```
M1 file-tier → M2 params → M3 catalog → M4 multi-file+refs → M5 merge-into-existing
 (one recipe → one real file)              (a real Feature)       (touch existing code)
```

**M1, concretely:** one catalog recipe — "Create Model" — emits the `FavoriteArtist` entity as a
real owned `.cs` file, params hand-fed (as the matcher eventually would). The smallest thing
unmistakably *on the north-star path* rather than a toy.

## Decisions locked

1. **Unit = Component** (Model/Service/Repo/API/DI); **Feature = composition + glue**. Granularity
   flexible — a whole-feature recipe is fine when the pattern is standard. *(May revisit as we go.)*
2. **Recipe = class** (named, param-schema'd, wrapping a node tree); the **snippet stays a method**.
3. **emit-new first**; **merge-into-existing** after, but designed-for from now.

## Guardrails (unchanged)

- Spike stays isolated (outside `dirs.proj` / `ABox.slnx`).
- Every change keeps the generate → compile → run gate green; emit-new output stays owned + readable.
- YAGNI — model the component in front of us, not all of C#.
