using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace DocEngineGuard;

// Proves the extracted doc-engine is self-enforcing with NOTHING from tests/Harness — no ParityGuard, no
// Rulebook engine, no [Rule] parity. Plain xUnit shelling out to the built engine (ADR 0015: run it as a
// process, never reference it). Three guarantees: the catalog is self-consistent, a good instance passes,
// and a bad instance is actually rejected (so the gate has teeth). Instances are written to the OS temp dir,
// never committed under the repo, so the host repo's own Docs test never discovers them.
public sealed class DocEngineGuardTests
{
    private static readonly string EngineDir = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .First(a => a.Key == "EngineDir").Value!;

    private static readonly string Config =
        new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;

    private static readonly Lazy<bool> Built = new(() =>
        Run("build", EngineDir, "-c", Config).Exit == 0
            ? true
            : throw new InvalidOperationException($"engine build failed:\n{Run("build", EngineDir, "-c", Config).Output}"));

    private const string GoodDoc = """
        ---
        docType: research
        status: draft
        ---

        ## Summary

        A minimal research instance that exercises the extracted engine's structural gate.

        ## Questions
        ### Does the extracted engine validate a conforming instance?
        It must accept a document that carries every required block.

        ## Outcome

        Yes — the required summary, question, and outcome blocks are present, so it conforms.
        """;

    // Missing the required `question` and `outcome` blocks — the validator must reject it.
    private const string BadDoc = """
        ---
        docType: research
        status: draft
        ---

        ## Summary

        Only a summary — the required question and outcome blocks are absent.
        """;

    [Fact]
    public void Catalog_is_self_consistent()
    {
        Assert.True(Built.Value);
        var r = Engine("check", "--root", EngineDir);
        Assert.True(r.Exit == 0, $"`docengine check` failed:\n{r.Output}");
    }

    [Fact]
    public void Valid_instance_passes()
    {
        Assert.True(Built.Value);
        var r = Engine("validate", Write("good.research.md", GoodDoc), "--root", EngineDir);
        Assert.True(r.Exit == 0, $"a conforming instance was rejected:\n{r.Output}");
    }

    [Fact]
    public void Invalid_instance_is_rejected()
    {
        Assert.True(Built.Value);
        var r = Engine("validate", Write("bad.research.md", BadDoc), "--root", EngineDir);
        Assert.True(r.Exit != 0, $"a non-conforming instance was accepted — the gate has no teeth:\n{r.Output}");
    }

    private static Result Engine(params string[] args) =>
        Run(new[] { "run", "--project", EngineDir, "--no-build", "-c", Config, "--" }.Concat(args).ToArray());

    private static string Write(string name, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"docengine-guard-{Guid.NewGuid():N}-{name}");
        File.WriteAllText(path, content);
        return path;
    }

    private sealed record Result(int Exit, string Output);

    private static Result Run(params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start dotnet");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return new Result(p.ExitCode, (stdout.Result + stderr.Result).Trim());
    }
}
