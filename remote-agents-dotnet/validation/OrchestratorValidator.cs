using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RemoteAgents.Validation;

namespace RemoteAgents.Validation.Orchestrator;

// Syntax-only Roslyn parse over every .cs file under
// remote-agents-dotnet/. Equivalent to the `node --check` walker in
// remote-agents/orchestrator/validation/orchestrator.mjs.
//
// Per the build plan, this is intentionally narrow — we don't reach for
// MSBuild here. Compile failures are caught by `dotnet build` upstream.
// What we care about is "does this parse?" so that a broken .cs landed
// by an agent gets flagged before the flow proceeds.
public sealed class OrchestratorValidator : IValidator
{
    private static readonly string[] SkipDirs =
    [
        "bin", "obj", ".git", "sessions",
    ];

    // Enable file-based-program directives (`#:project`, `#:property`) so
    // flows/*.cs parses without errors. Library .cs files don't use them
    // but the parser tolerates the feature flag in either case.
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithFeatures(new[] { new KeyValuePair<string, string>("FileBasedProgram", "true") });

    public Task<ValidationResult> ValidateAsync(string projectDir, CancellationToken ct = default)
    {
        var root = ResolveOrchestratorRoot(projectDir);
        if (!Directory.Exists(root))
        {
            return Task.FromResult(new ValidationResult(
                Ok: false,
                Summary: $"remote-agents-dotnet/ not found under {projectDir}",
                Errors: ""));
        }

        var errors = new StringBuilder();
        var fileCount = 0;
        var badFileCount = 0;

        foreach (var file in EnumerateCsFiles(root))
        {
            ct.ThrowIfCancellationRequested();
            fileCount++;
            try
            {
                var text = File.ReadAllText(file);
                var tree = CSharpSyntaxTree.ParseText(text, options: ParseOptions, path: file);
                var diags = tree.GetDiagnostics(ct).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                if (diags.Count > 0)
                {
                    badFileCount++;
                    foreach (var d in diags)
                    {
                        errors.AppendLine($"{file}: {d}");
                    }
                }
            }
            catch (Exception ex)
            {
                badFileCount++;
                errors.AppendLine($"{file}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        var ok = badFileCount == 0;
        var summary = ok
            ? $"OK — {fileCount} .cs files parsed cleanly under {root}"
            : $"FAIL — {badFileCount}/{fileCount} .cs files had syntax errors under {root}";

        return Task.FromResult(new ValidationResult(ok, summary, errors.ToString()));
    }

    // Find remote-agents-dotnet/ — projectDir is usually the repo root, but
    // tolerate being passed the orchestrator dir itself for tests.
    private static string ResolveOrchestratorRoot(string projectDir)
    {
        var direct = Path.Combine(projectDir, "remote-agents-dotnet");
        if (Directory.Exists(direct)) return direct;
        if (Path.GetFileName(projectDir.TrimEnd('\\', '/')).Equals("remote-agents-dotnet", StringComparison.OrdinalIgnoreCase))
            return projectDir;
        return direct; // returns a missing path; caller surfaces via summary
    }

    private static IEnumerable<string> EnumerateCsFiles(string root)
    {
        foreach (var dir in Walk(root))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.TopDirectoryOnly))
                yield return file;
        }
    }

    private static IEnumerable<string> Walk(string dir)
    {
        yield return dir;
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var name = Path.GetFileName(sub);
            if (Array.IndexOf(SkipDirs, name) >= 0) continue;
            foreach (var inner in Walk(sub)) yield return inner;
        }
    }
}
