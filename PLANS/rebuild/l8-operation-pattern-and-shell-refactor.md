# L8 — Operation pattern + Shell refactor (working plan)

**Status:** planned, not yet applied. Refactors code already committed on branch
`git-tooling` (`eb90174` — the first-cut Git actor). Iterate on this doc; the
checklists + "Decisions" are authoritative for what we agreed and *why*.

This pins down two coupled cleanups that surfaced while reviewing the first-cut
Git actor:

1. **The operation pattern** every actor (Agent, Git, and all future tooling —
   validators, reviews, isolation) must follow.
2. **The `RunCommand` → `Shell` refactor** + `EnsureOk` cleanup.

---

## Decisions (the converged answers — don't re-litigate without new info)

### D1 · Operation shape: named, nested op class with logic inline

Every operation is a **named, nested, private `sealed class` implementing
`IOperation<T>`**, with its logic written directly in `Execute`. This is the
pattern `Agent.RunOperation` already uses; Git converges onto it.

- **No `Operation.Of(name, Func<…>)` lambda helper.** We deliberately do *not*
  ship a terse minting verb.
- **No separate command layer (`GitCommands`).** The op class *is* the home for
  the logic — routing `XOp.Execute → GitCommands.XAsync` just moves complexity
  sideways. Logic used by exactly one op lives as a `private static` **inside
  that op**. Extract a shared helper only on a genuine **second** use.

**Why this shape (the deciding lens — the author is an AI agent):**
- An AI follows *structure* reliably and *prose conventions* unreliably. A
  body-arity rule ("inline if trivial, extract a method if ≥2 statements") is a
  per-op judgment call that an AI erodes over sessions — it grows a closure
  inline because it compiles, nothing fails, and it may not re-read the rule.
- The strongest enforcement an AI respects is **"there is no other way."** With
  no `Operation.Of` in the codebase, the only way to satisfy `IOperation<T>` is
  the class template — there's no terser path to drift into. A fat op is a
  conspicuous class, visible in any diff; "no fat lambdas" is not mechanically
  checkable, but "every operation is a named type" is (a one-line test/analyzer,
  if drift ever appears).
- Bonus wins a named class gives that matter more when a machine wrote the code
  and a human debugs it: real stack frames (`Git.CommitOp.Execute`, not
  `<>c__DisplayClass`), explicit inputs-as-fields (the reserved
  `OperationRecord` trace seam, ADR 0003 §4), and isolated testability.

**Cost we accept:** more types/lines than the lambda form → heavier to read and
more AI context per file. Mild tension with the "no micro-classes for symmetry"
standard — judged to be a *human-ergonomics* rule that partially inverts when an
AI writes uniform, single-responsibility dispatchers. Recorded in the ADR
amendment (D3).

**Allocation/perf is a non-issue and was *not* a deciding factor** either way:
operations are orchestration glue around multi-second external processes; a
couple of gen-0 allocations are noise. (Non-capturing lambdas are even
compiler-cached — so perf never favored one side. The decision is purely
enforceability.)

### D2 · `RunCommand` → `Shell.RunAsync`, and `EnsureOk()` drops its `op` param

- `RunCommand` is misleading: "Command" reads as a Command-pattern object, and
  `RunCommand.RunAsync` stutters. It actually runs a shell command line.
- Fold it into the existing `Shell` (which already owns shell-invocation helpers
  `CmdExePath`/`QuoteArg`): **`Shell.RunAsync(...)`**. Rename
  `RunCommandOptions`/`RunCommandResult` → `ShellOptions`/`ShellResult`.
- `EnsureOk(string op)`'s `op` arg is redundant + drift-prone — the result
  already carries `Command`. Drop the param; build the error from `Command` +
  `ExitCode`. Better message, no echoed labels at call sites.

### D3 · Amend ADR 0003

Record named-nested-op-classes as **the** pattern (ADR 0003 already leaned this
way via nesting-as-enforcement; we make it the rule and drop the "`Func` fallback
for one-off inline ops" allowance). Note the AI-author rationale (D1) and that
`Operation.Of` is intentionally *not* provided. Note enforcement of R-SPINE-1 is
unchanged (ops are nested in the actor; the drive/exec seam stays private).

---

## Target shapes (reference for implementation)

### Shell (`src/RemoteAgents/Tools/CommandLine/`)

```csharp
// Shell.cs — gains RunAsync alongside CmdExePath/QuoteArg
public static class Shell
{
    public static string CmdExePath => Path.Combine(Environment.SystemDirectory, "cmd.exe");
    public static string QuoteArg(string arg) { /* unchanged */ }

    public static async Task<ShellResult> RunAsync(
        string command, ShellOptions? options = null, CancellationToken ct = default)
    { /* body moved verbatim from RunCommand.RunAsync */ }
}

// ShellResult.cs
public sealed record ShellResult(
    string Command, int ExitCode, string Stdout, string Stderr, bool TimedOut, long DurationMs)
{
    public string ErrorText => string.IsNullOrEmpty(Stderr) ? Stdout : Stderr;

    public ShellResult EnsureOk() =>
        ExitCode == 0 ? this : throw new InvalidOperationException(
            $"`{Command}` failed (exit {ExitCode}): {ErrorText}");
}

// ShellOptions.cs — was RunCommandOptions, unchanged fields
public sealed record ShellOptions(
    string? Cwd = null, IDictionary<string, string?>? Env = null,
    int TimeoutMs = 5 * 60_000, string? Input = null);
```

### Git (`src/RemoteAgents/Actors/Git/Git.cs`)

```csharp
public sealed class Git
{
    public IOperation<DirtyResult> CheckDirty() => new CheckDirtyOp();
    public IOperation<DiffResult> Diff() => new DiffOp();
    public IOperation<ChangedFilesResult> ChangedFiles() => new ChangedFilesOp();
    public IOperation<GitCommitResult> Commit(string message, IReadOnlyList<string> files, string? coAuthor = null)
        => new CommitOp(message, files, coAuthor);
    public IOperation<GitPushResult> Push(string remote = "origin", string? branch = null, bool force = false)
        => new PushOp(remote, branch, force);

    private sealed class CheckDirtyOp : IOperation<DirtyResult>
    {
        public string Name => "git-dirty";
        public async Task<DirtyResult> Execute(FlowContext ctx, CancellationToken ct)
        {
            var res = (await Shell.RunAsync("git status --porcelain", new ShellOptions(Cwd: ctx.ProjectDir), ct)).EnsureOk();
            return new DirtyResult(res.Stdout.Trim().Length > 0);
        }
    }

    private sealed class ChangedFilesOp : IOperation<ChangedFilesResult>
    {
        public string Name => "git-changed-files";
        public async Task<ChangedFilesResult> Execute(FlowContext ctx, CancellationToken ct)
        {
            var res = (await Shell.RunAsync("git status --porcelain", new ShellOptions(Cwd: ctx.ProjectDir), ct)).EnsureOk();
            return new ChangedFilesResult(ParsePaths(res.Stdout));
        }
        private static IReadOnlyList<string> ParsePaths(string stdout) { /* porcelain parse */ }
    }

    private sealed class DiffOp : IOperation<DiffResult>
    {
        public string Name => "git-diff";
        public async Task<DiffResult> Execute(FlowContext ctx, CancellationToken ct)
        {
            var res = (await Shell.RunAsync("git diff", new ShellOptions(Cwd: ctx.ProjectDir), ct)).EnsureOk();
            return new DiffResult(res.Stdout, CountFiles(res.Stdout));
        }
        private static int CountFiles(string diff) { /* count "diff --git " */ }
    }

    private sealed class CommitOp(string message, IReadOnlyList<string> files, string? coAuthor)
        : IOperation<GitCommitResult>
    {
        public string Name => "git-commit";
        public async Task<GitCommitResult> Execute(FlowContext ctx, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("git commit: message is required", nameof(message));
            if (files.Count == 0)
                throw new ArgumentException("git commit: an explicit file list is required (no implicit add -A)", nameof(files));
            // add (explicit files) → commit -F - (stdin) → rev-parse HEAD → GitCommitResult
        }
        private static string FirstLine(string message) { /* … */ }
    }

    private sealed class PushOp(string remote, string? branch, bool force) : IOperation<GitPushResult>
    {
        public string Name => "git-push";
        public async Task<GitPushResult> Execute(FlowContext ctx, CancellationToken ct)
        {
            if (force && IsProtected(branch))
                throw new InvalidOperationException($"git push: refusing to force-push to {branch}");
            var target = branch ?? await CurrentBranchAsync(ctx.ProjectDir, ct);
            if (force && IsProtected(target))
                throw new InvalidOperationException($"git push: refusing to force-push to {target}");
            // git push [--force-with-lease] <remote> <target> → GitPushResult
        }
        private static bool IsProtected(string? b) => b is "main" or "master";
        private static async Task<string> CurrentBranchAsync(string dir, CancellationToken ct) { /* rev-parse --abbrev-ref HEAD */ }
    }
}
```

Agent already conforms (`RunOperation` nested, logic in `Execute`) — keep as is.

---

## Work plan (coherent commits, in order)

### Commit 1 — Shell refactor (mechanical, compiler-guided)
- [ ] Rename `RunCommand` → `Shell.RunAsync` (fold the method into `Shell`);
      delete `RunCommand.cs`.
- [ ] `RunCommandOptions` → `ShellOptions`; `RunCommandResult` → `ShellResult`.
- [ ] `EnsureOk(string op)` → `EnsureOk()` using `Command` + `ExitCode`.
- [ ] Update all call sites (grep `RunCommand`): the Git actor, `SubscriptionGuard`,
      `TempGitRepo`, plus any others the grep finds.
- [ ] Build warning-free; full test suite green.

### Commit 2 — Git ops → named nested op classes
- [ ] Delete the `GitOperation<T>` lambda adapter.
- [ ] Convert each of the 5 ops to a nested `sealed class …Op : IOperation<T>`
      with logic inline in `Execute` (projectDir → `ctx.ProjectDir`).
- [ ] Move each op-specific private static into the op that uses it
      (`ParsePaths`→ChangedFilesOp, `CountFiles`→DiffOp,
      `IsProtected`+`CurrentBranchAsync`→PushOp, `FirstLine`→CommitOp).
- [ ] Tests unchanged in intent (they already call `.Execute(ctx, ct)`); confirm
      `GitGuardrailTests` + `GitTests` still pass.
- [ ] Build warning-free; full suite green.

### Commit 3 — ADR 0003 amendment (D3)
- [ ] Record the operation pattern + AI-author rationale; remove the `Func`
      fallback allowance; note `Operation.Of` is intentionally absent.

---

## Flagged (separate — decide before/at the relevant point, not silently)

- **Op-name identity (latent bug).** Names are magic strings, and `Agent` reuses
  `config.Name` → two agent ops in one flow collide on ledger identity. Consider
  name constants + per-call-unique agent op names (e.g. `"{role}:review"`).
  *Decision pending: fold in now vs. its own change.*
- **`FlowContext` has no scoped `ProjectDir`.** `IsolationScope` (later L8) needs
  a deliberate `WithProjectDir`/child-scope seam on `FlowContext` (currently
  immutable, private ledger). No minting pattern solves this — it's its own small
  task when isolation lands.

---

## Rest of L8 (after this refactor)
Validators as `Step<ValidationResult>` (Orchestrator + Unity), `IsolationScope`,
`Reviews`/verdict — each following D1 (named nested op classes).

---

## Current state on `main` (as-built, 2026-06-04)

The Git actor is merged to `main` as the **first cut** — the refactors above (D1/D2)
are **not yet applied**:

- 5 operations (`CheckDirty`, `Diff`, `ChangedFiles`, `Commit`, `Push`) minted via
  the **`GitOperation<T>` lambda adapter** (the very thing D1 replaces).
- Shell layer still named `RunCommand` / `RunCommandOptions` / `RunCommandResult`;
  `EnsureOk(op)` still takes the redundant `op` param (D2 not applied).
- Guardrails in place (FR-C7): `Commit` requires an explicit non-empty file list
  (no `add -A`); `Push` refuses `force` to `main`/`master`.
- Registered in DI (`AddSingleton<Git>()`); tests green (`GitTests`,
  `GitGuardrailTests`).

So the code is *behaviorally* done for v1 but *structurally* pre-refactor. Apply
D1/D2/D3 before building the rest of L8 on top.

## Open design questions (2026-06-04) — gate the identity work, not the shipped tooling

Surfaced while reviewing the actor/operation pattern + per-agent git identity. None
block the Git tooling as shipped; they gate the *next* steps (identity stamping +
the rest-of-L8 actors).

**1. The three lanes — keep them separate.**
- *Authorization / ownership* — CODEOWNERS + branch protection, **platform layer,
  not our plumbing.** It *consumes* a stamped identity; it does not produce one.
- *Provenance / identity-stamping* — **ours.** Put the right author/committer
  (+ optional `Co-Authored-By`) on a git op.
- *Authentication / push creds* — **ours, separate lane, parked.** Own parallelism
  story (credential-cache races); not part of the identity question.

**2. Per-agent git identity is real intent but PARKED.** Design intent
(`PLANS/agentic-sdlc-flow.md` §0.2 L4) is per-agent *distinct* identity; the
provenance-vs-authorization fork is **Q13 / §0.3a, parked 2026-05-30, revisit before
§0.2/§0.3** — downstream of the L8 spine. So **do not build identity state into
`Git` at L8.** When built, the mechanism is settled:
- per-process `GIT_AUTHOR_NAME/EMAIL` + `GIT_COMMITTER_NAME/EMAIL` via
  `ShellOptions.Env` (parallel-safe; set all four — author *and* committer).
- **never** mutate `git config --global`/local then restore — a shared file races
  across concurrent agents. §0.3a already names "env-var git author" as the route.

**3. Parallelism invariant (all tool ops, not just git).** An operation configures
itself **per-invocation** (env / args), never by mutating shared/ambient machine
state. Already how `Shell` is shaped — state it so no future op reaches for global
config.

**4. The delivery seam = the merge point of two debates.** *Where per-acting
identity lives and how it reaches `Execute`* is the one open seam: actor-field
(`Git` becomes a stateful instance) vs `FlowContext` (flow-scoped) vs op-arg
(per-call). This is the **same** question as "should the `Git` wrapper be a stateful
instance or bare ops / static factory." Rule emerging: **an actor is an instance iff
its ops close over instance state** — `Agent` yes (`provider` + `config`); `Git`
today no (identity parked). Decide the seam once; both fall out.

**5. `IOperation<TResult, TArgs>` explored and REJECTED — REOPENED 2026-06-04, see
[ADR 0005](../../design/adr/0005-operation-args-generic.md) (proposed).** The rejection below
evaluated the *single-generic + `Unit`* form; ADR 0005 adopts a *split-interface* form
(`IOperation<TResult>` + `IOperation<TArgs,TResult>`) on the reframing that the redirect
indirection — not `TArgs` — is the disease. The notes below stand for the form they judged.
Moving args ctor→method
param does **not** kill the op class (something must still implement `Execute` and
bind collaborators); it adds a `TArgs` record per op, taxes no-arg ops with `Unit`
ceremony, splits the call-site builder phrase, and regresses D1's AI-enforceability
("there is no other way" → a 3-way judgment per op). `IOperation<T>` stands. If an
itch remains, name it: boilerplate → re-litigate `Operation.Of`; tracing → build the
reserved `OperationRecord` seam (ADR 0003 §4). Neither needs `TArgs`.
