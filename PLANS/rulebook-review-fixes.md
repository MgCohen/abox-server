# Rulebook ⇄ doc-engine — review fix plan

Remediation plan for the thermonuclear review of the 3-PR stack:
**#89** `claude/rulebook-docengine` (doc-engine Rulebook model) →
**#90** `claude/rulebook-adr` (ADR 0013) →
**#92** `claude/docs-test-type` (Docs test type).

Each fix notes its target branch. Apply on the owning branch; the stack carries
fixes upward. Verify per fix with `docengine check` / `validate` and the Docs +
Meta suites before pushing.

| # | Sev | Fix | Branch | Files | Status |
|---|---|---|---|---|---|
| H1 | High | Label parser: fence-aware + top-level only (folds in M2) | #89 | `tools/doc-engine/DocValidator.cs` | ✅ done |
| ~~H2~~ | ~~High~~ | **Cut** — over-mechanism (see below) | — | — | dropped |
| M1 | Medium | Process runner: drain pipes concurrently | #92 | `Docs/Support/DocEngine.cs` | ✅ done |
| L1 | Low | Author missing `wire.test-template.md` | #89 | `tools/doc-engine/out/` | ✅ done |
| L2 | Low | Document field-order's order-dependency | #89 | `tools/doc-engine/SchemaChecker.cs` | ✅ done |
| Nit | Nit | Soften ADR Consequences "how" | #90 | `design/adr/0013-rulebook-as-document.md` | ✅ done |

---

## H1 — Label parser ignores fences + matches nested bullets
**Branch:** #89 · `tools/doc-engine/DocValidator.cs`

- **What:** Make `LabelsIn` fence-aware and top-level-only (folds in **M2**, the `^\s*-` indent match).
- **Why:** A `**Why:**` inside a ` ``` ` fence currently passes the required-label check; a fenced `**Bogus:**` falsely fails. Disagrees with `RulebookFormat.OutsideFences`. The two can't *share* code (Harness must not depend on the engine — ADR 0013), so duplicate the small fence algorithm deliberately.
- **How:**

```csharp
// BEFORE
private static readonly Regex LabelBullet = new(@"^\s*-\s+\*\*(?<label>[^:*]+):\*\*", RegexOptions.Multiline);

private static HashSet<string> LabelsIn(string body) =>
    LabelBullet.Matches(body).Select(m => m.Groups["label"].Value.Trim()).ToHashSet(StringComparer.Ordinal);
```
```csharp
// AFTER — top-level bullets only (^-, no leading ws), fenced regions skipped
private static readonly Regex LabelBullet = new(@"^-\s+\*\*(?<label>[^:*]+):\*\*");

private static HashSet<string> LabelsIn(string body)
{
    var labels = new HashSet<string>(StringComparer.Ordinal);
    var inFence = false;
    foreach (var line in body.Split('\n'))
    {
        if (line.StartsWith("```", StringComparison.Ordinal)) { inFence = !inFence; continue; }
        if (!inFence && LabelBullet.Match(line) is { Success: true } m)
            labels.Add(m.Groups["label"].Value.Trim());
    }
    return labels;
}
```
- **Expected:** Fenced label-shaped text ignored both ways; only top-level `- **Label:**` bullets count. Negative cases (fenced `Why`, fenced `Bogus`, nested `  - **Why:**`) behave correctly; `validate` still PASS on the three samples.

---

## H2 — CUT (over-mechanism)

The original proposal: make the shell-out config-driven (`ABOX_DOCENGINE_CONFIG`
env var) and edit the **protected** `ci.yml` to prebuild the tool in Release.

**Why cut:** the doc-engine is a YAML validator — `check`/`validate` produce
byte-identical output in Debug and Release, so there is no behaviour to diverge.
The fix would add a speculative config switch (against **YAGNI / least
mechanism**), a reviewed edit to a protected path, and a fragile coupling: once
CI prebuilds only Release, `dotnet run --no-build -c Debug` breaks unless the env
var is threaded perfectly — it introduces the coupling it claims to remove.

The one legitimate residual concern — a YamlDotNet restore inside `dotnet test`
— is hypothetical here (the runner has network for the whole job) and already
bounded: `BuildOnce` self-builds once, so per-file `validate` stays `--no-build`.

**Decision:** leave `DocEngine.cs` self-building Debug. No env var, no `ci.yml`
edit, no protected-path PR. If a restore-at-test-time ever actually bites a
runner, the minimal response is **one** CI line (`dotnet build tools/doc-engine`)
with no config switch — added on the second real signal, not pre-emptively.

---

## M1 — Pipe-deadlock in the process runner
**Branch:** #92 · `Docs/Support/DocEngine.cs`

- **What:** Read stdout/stderr concurrently before `WaitForExit`.
- **Why:** Sequential `ReadToEnd()` blocks if the child fills the stderr buffer (~4 KB Windows) first — likeliest on the `BuildOnce` failure path.
- **How:**

```csharp
// BEFORE
using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start dotnet");
var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
p.WaitForExit();
return new Result(p.ExitCode, output.Trim());
```
```csharp
// AFTER — both pipes drained concurrently
using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start dotnet");
var stdout = p.StandardOutput.ReadToEndAsync();
var stderr = p.StandardError.ReadToEndAsync();
p.WaitForExit();
return new Result(p.ExitCode, (stdout.Result + stderr.Result).Trim());
```
- **Expected:** No deadlock regardless of output size/platform; identical output today.

---

## L1 — Dangling sample link
**Branch:** #89 · `tools/doc-engine/out/`

- **What:** Author the missing `wire.test-template.md` (preferred — completes + dogfoods the arrowed type), or repoint the link.
- **Why:** `out/wire.rulebook.md:8` links to `wire.test-template.md`, which doesn't exist; violates the `links` block's `resolves` rubric.
- **How — create `out/wire.test-template.md`:**

```markdown
---
docType: test-template
testType: wire
---

## Summary
<!-- id: wire-tmpl-summary -->
Each Wire Rule is one endpoint contract — method + route + given → response — proven with a real HttpClient against the Host over WebApplicationFactory.

## Criteria

### one_contract
<!-- id: one-contract -->
Exactly one endpoint contract (method + route → result), not several bundled.

### observable
<!-- id: observable -->
Asserts observable wire behaviour (status, body shape, SSE), not an implementation detail.

### why_justifies
<!-- id: why-justifies -->
The Why gives the routing/serialization guarantee, not a restatement of the header.
```
- **Fallback (one-liner):** `out/wire.rulebook.md:8` → `[Wire test-template](./arch.test-template.md)`.
- **Expected:** `docengine validate out/wire.test-template.md` PASS; link resolves; the Docs `Instances_validate` test now also covers it.

---

## L2 — Field-order check leans on Dictionary enumeration order
**Branch:** #89 · `tools/doc-engine/SchemaChecker.cs`

- **What:** Add the one allowed "why" comment (load-bearing + non-obvious).
- **Why:** Correctness depends on map enumeration == YAML authored order; verified safe but not contractually guaranteed.
- **How:**

```csharp
// AFTER — prepend to CheckFieldOrder
// Map order == YAML file order: YamlDotNet yields an insertion-ordered Dictionary and Yaml.AsMap copies it as-is.
private static void CheckFieldOrder(List<string> errs, IReadOnlyDictionary<string, object?> defn,
                                    IReadOnlyDictionary<string, object?> fields)
{
```
- **Expected:** No behavior change; the implicit dependency is documented at the point of use.

---

## Nit — ADR 0013 borderline "how"
**Branch:** #90 · `design/adr/0013-rulebook-as-document.md` *(optional)*

- **What:** Soften the Consequences enumeration of exact blocks/doctypes.
- **How:** "…gains the `rule`/`links`/`criterion` vocabulary and `rulebook`/`test-template` doctypes (see `planned-doctypes.md` §2)" instead of the full mechanics list.
- **Expected:** Stays purely on the "why" side; no functional change.

---

## Sequencing

1. **#89:** H1 + L1 + L2 → verify (`check`, `validate` all `out/*.md`). ✅
2. **#90:** Nit → rebased onto #89. ✅
3. **#92:** M1 only (H2 cut) → verify (`dotnet test … ~Docs`, Meta suite). ✅

Each verified before push; the stack carries the fixes upward. #92 needs no
protected change.
