using System.Runtime.CompilerServices;

namespace Slice;

// The emit tool.
//   dotnet run -- live    lower the recipe into the throwaway preview dir (live/)
//   dotnet run -- emit     explicit gate: materialize owned files at the target (emitted/)
//   dotnet run -- prove    live -> emit -> edit-via-variant -> re-emit, with self-checks
//
// Paths are anchored on THIS source file via CallerFilePath so the slice root is
// found regardless of where bin/obj land — never escaping the folder, never
// leaking to the repo-root artifacts/ dir (the trap probe C flagged).
static class Program
{
    static int Main(string[] args)
    {
        var root = SliceRoot();
        var catalog = CatalogSource(root);
        var recipe = AuthoredRecipe.AddItemToCart();
        var emitTarget = Path.Combine(root, "emitted");
        var previewDir = Path.Combine(root, "live");

        var command = args.Length > 0 ? args[0] : "emit";
        return command switch
        {
            "live" => Live(recipe, catalog, previewDir),
            "emit" => Emit(recipe, catalog, emitTarget),
            "prove" => Prove(recipe, catalog, emitTarget, previewDir),
            _ => Fail($"unknown command '{command}'. Use: live | emit | prove"),
        };
    }

    static int Live(FeatureRecipe recipe, string catalog, string previewDir)
    {
        Directory.CreateDirectory(previewDir);
        Console.WriteLine($"LIVE  {recipe.FeatureName}  ->  preview '{Rel(previewDir)}'");
        foreach (var a in Emitter.Lower(recipe, catalog))
        {
            File.WriteAllText(Path.Combine(previewDir, a.RelativePath), a.Text);
            Console.WriteLine($"      regenerated {a.RelativePath}");
        }
        Console.WriteLine("      (preview is overwritten wholesale each run; never hand-edited)");
        return 0;
    }

    static int Emit(FeatureRecipe recipe, string catalog, string target)
    {
        Directory.CreateDirectory(target);
        Console.WriteLine($"EMIT  {recipe.FeatureName}  ->  target '{Rel(target)}' (namespace {recipe.Namespace})");
        foreach (var a in Emitter.Lower(recipe, catalog))
        {
            File.WriteAllText(Path.Combine(target, a.RelativePath), a.Text);
            Console.WriteLine($"      wrote {a.RelativePath}");
        }
        return 0;
    }

    static int Prove(FeatureRecipe recipe, string catalog, string target, string previewDir)
    {
        var ok = true;
        var handlerFile = Path.Combine(target, $"{recipe.FeatureName}.Handler.cs");

        Section("0. clean slate");
        if (Directory.Exists(previewDir)) Directory.Delete(previewDir, true);
        if (Directory.Exists(target)) Directory.Delete(target, true);
        Console.WriteLine("      removed live/ and emitted/");

        Section("1. LIVE (preview only — no leak across the gate)");
        Live(recipe, catalog, previewDir);
        ok &= Check(!Directory.Exists(target), "live did NOT create the owned target");

        Section("2. EMIT (explicit gate -> owned files)");
        Emit(recipe, catalog, target);
        var emitted = File.ReadAllText(handlerFile);
        ok &= Check(File.Exists(handlerFile), "emit created the owned handler file");
        ok &= Check(emitted.Contains("EnsureService<Repo<User>>"), "wiring (probe B): Repo<User> registered");
        ok &= Check(emitted.Contains("EnsureQuery<BookDetails>"), "wiring (probe B): BookDetails query registered");
        ok &= Check(File.ReadAllText(Path.Combine(target, $"{recipe.FeatureName}.Models.cs")).Contains("record CartItem"),
            "mint (probe A): CartItem record emitted to the model file");

        Section("3. EDIT recipe (variant +Notes field on CartItem) and re-run LIVE");
        var v2 = WithExtraField(recipe);
        Live(v2, catalog, previewDir);
        var previewModels = File.ReadAllText(Path.Combine(previewDir, $"{recipe.FeatureName}.Models.cs"));
        ok &= Check(previewModels.Contains("Notes"), "live preview tracked the recipe edit (+Notes)");
        ok &= Check(File.ReadAllText(handlerFile) == emitted, "DETACHMENT: emitted target untouched by live");

        Section("4. RE-EMIT v2 (override)");
        Emit(v2, catalog, target);
        var reemitted = File.ReadAllText(Path.Combine(target, $"{recipe.FeatureName}.Models.cs"));
        ok &= Check(reemitted.Contains("Notes"), "OVERRIDE: re-emit reflects the recipe edit");

        Section("4b. restore canonical emit (so the committed sample matches the authored recipe)");
        Emit(recipe, catalog, target);
        ok &= Check(!File.ReadAllText(Path.Combine(target, $"{recipe.FeatureName}.Models.cs")).Contains("Notes"),
            "re-emit clobbered the variant — committed sample is the canonical recipe");

        Console.WriteLine();
        Console.WriteLine(ok ? "PROVE: PASS" : "PROVE: FAIL");
        return ok ? 0 : 1;
    }

    // A recipe edit: add a Notes field to CartItem (proves live tracks edits).
    static FeatureRecipe WithExtraField(FeatureRecipe recipe)
    {
        var models = recipe.Models.Select(m => m.Name == "CartItem"
            ? m with { Fields = [.. m.Fields, new FieldSpec("Notes", "string")] }
            : m).ToList();
        return recipe with { Models = models };
    }

    // The catalog support source the emit tool resolves/scans against: the
    // hand-authored Catalog.cs + Runtime.cs, read from disk (anchored on this file).
    static string CatalogSource(string root)
    {
        var catalog = File.ReadAllText(Path.Combine(root, "src", "Catalog.cs"));
        var runtime = File.ReadAllText(Path.Combine(root, "src", "Runtime.cs"));
        return catalog + "\n" + runtime;
    }

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

    static string SliceRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, ".."));

    static string Rel(string path)
    {
        var idx = path.IndexOf("integration-slice", StringComparison.Ordinal);
        return idx >= 0 ? path[idx..] : path;
    }
}
