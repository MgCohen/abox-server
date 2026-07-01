using System.Runtime.CompilerServices;

namespace ProbeC;

// PROBE C — live vs emitted (the emit gate).
//
//   dotnet run -- live    regenerate the preview in place (the continuous self-generation)
//   dotnet run -- emit    explicit action: materialize the owned files at the configured target
//   dotnet run -- prove   run the full sequence (live -> emit -> edit+live -> re-emit) and report
//
// The recipe is RecipeSource.Current() (override its variant with PROBE_C_RECIPE_VARIANT=v1|v2 to
// simulate "the author edited the recipe"). The configured target is EmitTarget.Sample.
static class Program
{
    static int Main(string[] args)
    {
        var probeRoot = ProbeRoot();
        var previewDir = Path.Combine(probeRoot, "live");
        var target = EmitTarget.Sample(probeRoot);
        var recipe = RecipeSource.Current();

        var command = args.Length > 0 ? args[0] : "prove";
        return command switch
        {
            "live" => RunLive(recipe, target, previewDir),
            "emit" => RunEmit(recipe, target),
            "prove" => RunProve(probeRoot, previewDir, target),
            _ => Fail($"unknown command '{command}'. Use: live | emit | prove"),
        };
    }

    static int RunLive(Recipe recipe, EmitTarget target, string previewDir)
    {
        var written = Engine.Live(recipe, target, previewDir);
        Console.WriteLine($"LIVE  recipe={recipe.TypeName}({recipe.Fields.Count} fields)  ->  preview dir '{Rel(previewDir)}'");
        foreach (var p in written) Console.WriteLine($"      regenerated {Rel(p)}");
        Console.WriteLine("      (preview is never hand-edited; it is overwritten wholesale each run)");
        return 0;
    }

    static int RunEmit(Recipe recipe, EmitTarget target)
    {
        var written = Engine.Emit(recipe, target);
        Console.WriteLine($"EMIT  recipe={recipe.TypeName}({recipe.Fields.Count} fields)  ->  configured target");
        Console.WriteLine($"      folder    = {target.Folder}");
        Console.WriteLine($"      namespace = {target.Namespace}");
        foreach (var p in written) Console.WriteLine($"      wrote {Rel(p)}");
        return 0;
    }

    // The full demonstrated sequence, self-checking. Captures file states to prove detachment + override.
    static int RunProve(string probeRoot, string previewDir, EmitTarget target)
    {
        var ok = true;
        var customerFile = Path.Combine(target.Folder, "Customer.cs");

        Section("0. clean slate");
        if (Directory.Exists(previewDir)) Directory.Delete(previewDir, true);
        if (Directory.Exists(target.Folder)) Directory.Delete(target.Folder, true);
        Console.WriteLine("      removed live/ and the target folder");

        Section("1. LIVE on recipe v1 (continuous self-generation, preview only)");
        var v1 = WithVariant("v1");
        Engine.Live(v1, target, previewDir);
        Console.WriteLine($"      preview has {FieldCount(Path.Combine(previewDir, "Customer.cs"))} fields; target folder exists = {Directory.Exists(target.Folder)}");
        ok &= Check(!Directory.Exists(target.Folder), "live did NOT create the owned target (no leak across the gate)");

        Section("2. EMIT v1 (explicit gate -> owned files at configured target)");
        Engine.Emit(v1, target);
        var emittedV1 = File.ReadAllText(customerFile);
        Console.WriteLine($"      emitted {Rel(customerFile)} with {FieldCount(customerFile)} fields, namespace {NamespaceOf(customerFile)}");
        ok &= Check(File.Exists(customerFile), "emit created the owned file at the configured target");
        ok &= Check(NamespaceOf(customerFile) == target.Namespace, $"emitted file declares the configured namespace ({target.Namespace})");

        Section("3. EDIT the recipe to v2 (+Email) and re-run LIVE");
        var v2 = WithVariant("v2");
        Engine.Live(v2, target, previewDir);
        var previewV2 = FieldCount(Path.Combine(previewDir, "Customer.cs"));
        var targetAfterLive = File.ReadAllText(customerFile);
        Console.WriteLine($"      preview now has {previewV2} fields (followed the recipe edit)");
        Console.WriteLine($"      emitted target still has {FieldCount(customerFile)} fields");
        ok &= Check(previewV2 == 4, "live preview tracked the recipe edit (3 -> 4 fields)");
        ok &= Check(targetAfterLive == emittedV1, "DETACHMENT: the emitted target file is byte-identical to before — live did not touch it");

        Section("4. RE-EMIT v2 (explicit gate again -> overrides the target)");
        Engine.Emit(v2, target);
        var emittedV2 = File.ReadAllText(customerFile);
        Console.WriteLine($"      target now has {FieldCount(customerFile)} fields");
        ok &= Check(emittedV2 != emittedV1, "OVERRIDE: re-emit changed the target file");
        ok &= Check(FieldCount(customerFile) == 4, "re-emitted target reflects recipe v2 (4 fields)");

        Section("4b. RE-EMIT clobbers a manual edit (honest limitation, by design)");
        File.WriteAllText(customerFile, emittedV2 + "\n// a human hand-edited this emitted file\n");
        Console.WriteLine("      hand-edited the emitted file (added a trailing comment)");
        Engine.Emit(v2, target);
        var afterReemit = File.ReadAllText(customerFile);
        ok &= Check(!afterReemit.Contains("hand-edited"), "re-emit CLOBBERED the manual edit (no reconciliation — accepted)");

        Console.WriteLine();
        Console.WriteLine(ok ? "PROVE: PASS" : "PROVE: FAIL");
        return ok ? 0 : 1;
    }

    // --- helpers ---------------------------------------------------------------------------------

    static Recipe WithVariant(string variant)
    {
        var prev = Environment.GetEnvironmentVariable("PROBE_C_RECIPE_VARIANT");
        Environment.SetEnvironmentVariable("PROBE_C_RECIPE_VARIANT", variant);
        var recipe = RecipeSource.Current();
        Environment.SetEnvironmentVariable("PROBE_C_RECIPE_VARIANT", prev);
        return recipe;
    }

    static int FieldCount(string file) =>
        File.ReadLines(file).Count(l => l.Contains("{ get; init; }"));

    static string NamespaceOf(string file) =>
        File.ReadLines(file).First(l => l.StartsWith("namespace ")).Replace("namespace ", "").TrimEnd(';');

    static bool Check(bool condition, string label)
    {
        Console.WriteLine($"      [{(condition ? "PASS" : "FAIL")}] {label}");
        return condition;
    }

    static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {title} ---");
    }

    static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    // Anchor on THIS source file (like the spike's Snippets.SourcePath via CallerFilePath), so the
    // probe root is found regardless of where the build artifacts land — never escaping the folder.
    static string ProbeRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, ".."));

    static string Rel(string path)
    {
        var idx = path.IndexOf("probe-c-live-emit", StringComparison.Ordinal);
        return idx >= 0 ? path[idx..] : path;
    }
}
