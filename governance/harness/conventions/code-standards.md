# Code standards

Judgment-call rules we operate by. Mechanical style (formatting, naming) moves
into `.editorconfig` later. Applied **going forward**.

**Architecture / spine**

- **YAGNI / least mechanism.** Build for the requirement in front of you — no
  speculative abstraction, config, or extensibility "for later." Add the
  abstraction on the *second* real use, not the first. (The assembly-wall
  collapse is the worked example.) **Exception: basic infrastructure that defines
  the repo's architecture** — the structural guardrails that keep drift out (arch
  rulebook, placement guards, the test taxonomy) earn their place on the *first*
  use, because their whole job is to exist before the second use slips through.
- **DI services over statics.** Construct collaborators from the container; no
  hidden static singletons.
- **Results own their display** via `ToString()` — no per-call `summarize` lambda.
- **Test doubles live with the test that uses them.** Fakes and stubs stay local
  to the consuming test; promote to a shared location only when genuinely reused
  (a shared harness/fixture).
- **Throw actionable errors; never swallow them silently.** Messages say what to
  do; an intentional ignore gets a one-line *why*.
- **Label provisional/scaffolding code as provisional** (e.g. `DelayStep` / the stub
  flow) so it's never mistaken for settled design.
- **Per layer:** warning-free build + green tests + behavior verified (run it,
  not just compile) + one coherent commit. Nullable on, warnings-as-errors,
  file-scoped namespaces, net10.0.
- Honor the PRD's spine + architecture rules (R-SPINE-1/2, R-ARCH-1/2/3) and
  non-goals — validators are Steps, no `new Agent()` in composition,
  contracts/guards live with their layer.

**Craft / quality**

- **Prefer deleting to adding.** No dead or commented-out code — delete it; git
  remembers.
- **Clarity over cleverness.** Optimize for the next reader: obvious names,
  straight-line logic.
- **Make illegal states unrepresentable.** Lean on the type system — non-null,
  records, enums, small value types — so bad states don't compile.
- **Single type per file** — except nested types and a generic + its companion.
- **No comments.** Code carries its meaning through names and structure — no
  XML-doc summaries, no narration of what the code already says, no section
  banners. Exactly two comments are allowed: (a) a one-line non-obvious *why* the
  code genuinely cannot express, and (b) an oracle / Tier-A citation on a ported
  tricky bit. Reaching for anything else means rename or restructure instead.
- **Small, focused methods and classes.**
