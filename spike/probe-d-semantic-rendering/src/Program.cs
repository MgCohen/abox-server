using System.Runtime.CompilerServices;

namespace ProbeD;

// PROBE D — semantic-model type rendering (the naming fix).
//
//   dotnet run            run the whole proof: render both styles, derive usings, compile-check
//
// Reads the author-facing recipe (Sample.Recipe), renders the record two ways via the semantic
// model, writes both to out/*.cs, and compile-checks each. Prints a per-case table so every hard
// type case (alias, framework, nested, generic, nullable value+ref, tuple) is visible in both styles.
static class Program
{
    static int Main()
    {
        var probeRoot = ProbeRoot();
        var outDir = Path.Combine(probeRoot, "out");
        Directory.CreateDirectory(outDir);

        var recipe = Sample.Recipe;
        var support = Sample.SupportTypes;
        var ns = "ProbeD.Sample";

        var renderer = SemanticRenderer.Create(support);

        Section("0. the authored recipe (this is ALL the author writes — no Roslyn in sight)");
        Console.WriteLine($"      record {recipe.Name}");
        foreach (var f in recipe.Fields)
            Console.WriteLine($"        {f.Name,-9}: TypeRef(\"{f.Type.Text}\")");

        Section("1. per-field rendering — idiomatic vs fully-qualified (semantic model)");
        Console.WriteLine($"      {"field",-9} {"recipe text",-26} {"idiomatic",-28} fully-qualified");
        foreach (var f in recipe.Fields)
        {
            var sym = renderer.Resolve(f.Type);
            var idiom = SemanticRenderer.RenderIdiomatic(sym);
            var fq = SemanticRenderer.RenderFullyQualified(sym);
            Console.WriteLine($"      {f.Name,-9} {f.Type.Text,-26} {idiom,-28} {fq}");
        }

        Section("2. idiomatic emit (UseSpecialTypes + nested '.' + derived usings)");
        var idiomatic = RecordEmitter.Idiomatic(recipe, renderer, ns);
        var idiomaticPath = Path.Combine(outDir, $"{recipe.Name}.Idiomatic.cs");
        File.WriteAllText(idiomaticPath, idiomatic.Source);
        Console.WriteLine($"      derived usings: {(idiomatic.Usings.Count == 0 ? "(none)" : string.Join(", ", idiomatic.Usings))}");
        Console.WriteLine($"      wrote {Rel(idiomaticPath)}");
        Indent(idiomatic.Source);

        Section("3. fully-qualified emit (global::… ; no usings)");
        var fq2 = RecordEmitter.FullyQualified(recipe, renderer, ns);
        var fqPath = Path.Combine(outDir, $"{recipe.Name}.FullyQualified.cs");
        File.WriteAllText(fqPath, fq2.Source);
        Console.WriteLine($"      derived usings: {(fq2.Usings.Count == 0 ? "(none — fully-qualified is self-contained)" : string.Join(", ", fq2.Usings))}");
        Console.WriteLine($"      wrote {Rel(fqPath)}");
        Indent(fq2.Source);

        Section("4. compile-check BOTH emitted variants (Roslyn — the gate)");
        var ok = true;
        ok &= Gate("idiomatic", CompileCheck.Compile(idiomatic.Source, support));
        ok &= Gate("fully-qualified", CompileCheck.Compile(fq2.Source, support));

        Console.WriteLine();
        Console.WriteLine(ok ? "PROBE D: PASS" : "PROBE D: FAIL");
        return ok ? 0 : 1;
    }

    static bool Gate(string label, CompileCheck.Result result)
    {
        Console.WriteLine($"      [{(result.Ok ? "PASS" : "FAIL")}] {label} emit compiles");
        foreach (var e in result.Errors)
            Console.WriteLine($"             {e}");
        return result.Ok;
    }

    static void Indent(string source)
    {
        foreach (var line in source.TrimEnd().Split('\n'))
            Console.WriteLine($"        | {line.TrimEnd()}");
    }

    static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {title} ---");
    }

    // Anchor on THIS source file (probe-c's CallerFilePath trick) so out/ lands inside the probe
    // folder regardless of where build artifacts go — never leaking .cs to the repo root.
    static string ProbeRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, ".."));

    static string Rel(string path)
    {
        var idx = path.IndexOf("probe-d-semantic-rendering", StringComparison.Ordinal);
        return idx >= 0 ? path[idx..] : path;
    }
}
