# Rulebook ⇄ doc-engine — review fix plan

Remediation plan for the thermonuclear review of the 3-PR stack:
**#89** `claude/rulebook-docengine` (doc-engine Rulebook model) →
**#90** `claude/rulebook-adr` (ADR 0013) →
**#92** `claude/docs-test-type` (Docs test type).

Each fix notes its target branch. Apply on the owning branch; the stack carries
fixes upward. Verify per fix with `docengine check` / `validate` and the Docs +
Meta suites before pushing.

| # | Sev | Fix | Branch | Files |
|---|---|---|---|---|
| H1 | High | Label parser: fence-aware + top-level only (folds in M2) | #89 | `tools/doc-engine/DocValidator.cs` |
| H2 | High | Docs shell-out: config-driven + CI prebuild | #92 | `Docs/Support/DocEngine.cs`, `.github/workflows/ci.yml` (protected) |
| M1 | Medium | Process runner: drain pipes concurrently | #92 | `Docs/Support/DocEngine.cs` |
| L1 | Low | Author missing `wire.test-template.md` | #89 | `tools/doc-engine/out/` |
| L2 | Low | Document field-order's order-dependency | #89 | `tools/doc-engine/SchemaChecker.cs` |
| Nit | Nit | Soften ADR Consequences "how" | #90 | `design/adr/0013-rulebook-as-document.md` |

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

## H2 — Docs shell-out builds/restores at test time, Debug pinned vs Release CI
**Branch:** #92 · `Docs/Support/DocEngine.cs` + `.github/workflows/ci.yml` *(protected — owner-reviewed)*

- **What:** (a) Make the shell-out config-driven; (b) prebuild+restore the out-of-solution tool in CI so the test phase neither restores nor builds.
- **Why:** Tool isn't in `ABox.slnx`, so `dotnet test --no-build` triggers a Debug build + YamlDotNet restore *during tests* — needs network + a writable build at test time, and `-c Debug` diverges from CI's Release pipeline. Classic "green locally, red on offline/Windows runner."
- **How — code:**

```csharp
// BEFORE
public static Result Run(params string[] args)
{
    _ = Built.Value;
    return Exec(new[] { "run", "--project", ProjectDir, "--no-build", "-c", "Debug", "--" }.Concat(args).ToArray());
}
private static bool BuildOnce()
{
    var r = Exec(new[] { "build", ProjectDir, "-c", "Debug" });
    ...
}
```
```csharp
// AFTER — config from env (CI=Release, local default Debug)
private static readonly string Config =
    Environment.GetEnvironmentVariable("ABOX_DOCENGINE_CONFIG") ?? "Debug";

public static Result Run(params string[] args)
{
    _ = Built.Value;
    return Exec(new[] { "run", "--project", ProjectDir, "--no-build", "-c", Config, "--" }.Concat(args).ToArray());
}
private static bool BuildOnce()
{
    var r = Exec(new[] { "build", ProjectDir, "-c", Config });
    if (r.Exit != 0)
        throw new InvalidOperationException($"Could not build the doc-engine at {ProjectDir} ({Config}):\n{r.Output}");
    return true;
}
```
- **How — CI** (confirm exact step names against `ci.yml`; protected):

```yaml
# BEFORE
- run: dotnet build ABox.slnx -c Release
- run: dotnet test  ABox.slnx -c Release --no-build

# AFTER
- run: dotnet build ABox.slnx -c Release
- run: dotnet build tools/doc-engine -c Release   # prebuild+restore the out-of-solution tool
- run: dotnet test  ABox.slnx -c Release --no-build
  env:
    ABOX_DOCENGINE_CONFIG: Release
```
- **Expected:** Restore happens in the build phase; test-phase `BuildOnce` is an incremental no-op; `Run` uses the prebuilt Release output. No network/build coupling inside `dotnet test`. Local defaults to Debug, unchanged.
- **Alt (no CI edit):** drop `--no-build` and `-c` so the tool self-builds Debug once at test time. Simpler, but keeps the restore-at-test-time risk. Prefer the CI prebuild.

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

1. **#89:** H1 + L1 + L2 → verify (`check`, `validate` all `out/*.md`).
2. **#90:** Nit (optional).
3. **#92:** H2 + M1 → verify (`dotnet test … ~Docs`, Meta suite).

Each verified before push; the stack carries the fixes upward.
