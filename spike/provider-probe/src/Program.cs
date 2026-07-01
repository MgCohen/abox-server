using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Probe;

// The probe.
//   dotnet run -- emit    emit both owned handlers (Repo + Bucket) from the two recipes
//   dotnet run -- prove   emit + assert the two outputs + the compiler REJECTS a bad swap
//
// The two recipes differ by ONE token (via) plus the key it forces. Both type-check; the
// emitter SUBSTITUTES the `mutate` snippet (not string-built) and rewrites the canonical
// verbs per the StoreCatalog, so each lowers to a different owned handler; and a mismatched
// key does not compile (the negative check). Paths anchored via CallerFilePath.
static class Program
{
    static int Main(string[] args)
    {
        var root = ProbeRoot();
        var emitTarget = Path.Combine(root, "emitted");

        var command = args.Length > 0 ? args[0] : "emit";
        return command switch
        {
            "emit" => Emit(root, emitTarget),
            "prove" => Prove(root, emitTarget),
            _ => Fail($"unknown command '{command}'. Use: emit | prove"),
        };
    }

    static (Emitter.Artifact repo, Emitter.Artifact bucket) LowerBoth(string root)
    {
        var repo = Emitter.Lower(
            Lift.From(File.ReadAllText(Path.Combine(root, "src", "RepoRecipe.cs")), "AddPoints"));
        var bucket = Emitter.Lower(
            Lift.From(File.ReadAllText(Path.Combine(root, "src", "BucketRecipe.cs")), "AddPoints"));
        return (repo, bucket);
    }

    static int Emit(string root, string target)
    {
        Directory.CreateDirectory(target);
        var (repo, bucket) = LowerBoth(root);
        foreach (var a in new[] { repo, bucket })
        {
            File.WriteAllText(Path.Combine(target, a.RelativePath), a.Text);
            Console.WriteLine($"EMIT  wrote {a.RelativePath}");
        }
        return 0;
    }

    static int Prove(string root, string target)
    {
        var ok = true;
        Directory.CreateDirectory(target);
        var (repo, bucket) = LowerBoth(root);
        File.WriteAllText(Path.Combine(target, repo.RelativePath), repo.Text);
        File.WriteAllText(Path.Combine(target, bucket.RelativePath), bucket.Text);

        Section("1. ONE recipe shape, TWO owned handlers — provider-specific load/save");
        ok &= Check(repo.Text.Contains("Handle(Repo<User> repo, AddPointsCommand command)"),
            "Repo handler takes Repo<User>");
        ok &= Check(repo.Text.Contains("var __key = command.Email;"),
            "Repo key is a string (command.Email), bound once");
        ok &= Check(repo.Text.Contains("var agg = repo.Load(__key);"), "Repo load = repo.Load");
        ok &= Check(repo.Text.Contains("repo.Store(__key, agg);"), "Repo save = repo.Store");

        ok &= Check(bucket.Text.Contains("Handle(Bucket<User> bucket, AddPointsCommand command)"),
            "Bucket handler takes Bucket<User>");
        ok &= Check(bucket.Text.Contains("var __key = new BucketKey(command.Region);"),
            "Bucket key is a BucketKey (forced by the provider), bound once");
        ok &= Check(bucket.Text.Contains("var agg = bucket.Download(__key);"), "Bucket load = bucket.Download");
        ok &= Check(bucket.Text.Contains("bucket.Upload(__key, agg);"), "Bucket save = bucket.Upload");

        Section("2. SAME shape + SAME output — the scaffold came from ONE snippet, body lifted");
        ok &= Check(repo.Text.Contains("agg.AddPoints(command.Points);")
                 && bucket.Text.Contains("agg.AddPoints(command.Points);"),
            "identical lifted body in both handlers");
        ok &= Check(repo.Text.Contains("return agg;") && bucket.Text.Contains("return agg;"),
            "both return the aggregate (same output type: User) — from the snippet's `return agg;`");

        Section("3. THE SNIPPET IS REAL C# — the scaffold is authored, not string-built");
        var snippetSrc = File.ReadAllText(Snippets.SourcePath);
        ok &= Check(snippetSrc.Contains("[Snippet(\"mutate\")]"), "mutate is a [Snippet] method (like Loop)");
        ok &= Check(snippetSrc.Contains("var agg = @store.Get(__key);")
                 && snippetSrc.Contains("@store.Save(__key, agg);"),
            "the scaffold body is real, compiling C# over the canonical Get/Save verbs");
        ok &= Check(snippetSrc.Contains("Block.Of(\"body\")"), "the body is a Block.Of(\"body\") slot");
        var emitterSrc = File.ReadAllText(Path.Combine(root, "src", "Emitter.cs"));
        ok &= Check(!emitterSrc.Contains("var agg =") && !emitterSrc.Contains("return agg;"),
            "the emitter does NOT string-build the scaffold — it substitutes the snippet");

        Section("4. THE TYPE-SAFE SWAP — the compiler REJECTS a key that doesn't match the provider");
        var shared = new[] { "Store.cs", "Domain.cs", "Compose.cs" }
            .Select(f => File.ReadAllText(Path.Combine(root, "src", f))).ToArray();

        var matched = Errors(shared, Snippet(key: "c => new BucketKey(c.Region)"));
        ok &= Check(matched.Count == 0, "MATCHED key (BucketKey) compiles against a Bucket provider");

        var mismatched = Errors(shared, Snippet(key: "c => c.Email"));
        ok &= Check(mismatched.Count > 0, "MISMATCHED key (string) is REJECTED against a Bucket provider");
        if (mismatched.Count > 0)
            Console.WriteLine($"      compiler said: {mismatched[0].GetMessage()}");

        Console.WriteLine();
        Console.WriteLine(ok ? "PROVE: PASS" : "PROVE: FAIL");
        return ok ? 0 : 1;
    }

    // The Bucket recipe surface with `key` parameterised, so we can vary it. With key =>
    // BucketKey it must compile; with key => string it must NOT (the swap is enforced by the
    // builder: via is IStore<BucketKey,User>, so key must be Func<TCmd,BucketKey>).
    static string Snippet(string key) => $$"""
        using Probe;
        using Probe.Domain;
        using static Probe.Compose;
        public static class __Neg
        {
            public static Node Recipe() =>
                new Feature<AddPointsCommand>(scope =>
                    Mutate(scope,
                        via:  Stores.BucketStore<User>(),
                        key:  {{key}},
                        body: (user, c) => user.AddPoints(c.Points)));
        }
        """;

    static IReadOnlyList<Diagnostic> Errors(string[] sharedSources, string snippet)
    {
        var options = new CSharpParseOptions(LanguageVersion.Latest);
        // The project builds with ImplicitUsings; the throwaway compilation must supply the
        // same global usings or the shared sources' Dictionary<,> etc. won't resolve.
        const string globals = """
            global using System;
            global using System.Collections.Generic;
            global using System.Linq;
            global using System.Threading.Tasks;
            """;
        var trees = sharedSources.Select(s => CSharpSyntaxTree.ParseText(s, options))
            .Append(CSharpSyntaxTree.ParseText(globals, options))
            .Append(CSharpSyntaxTree.ParseText(snippet, options));
        var compilation = CSharpCompilation.Create("Probe.Neg", trees, Net.ReferenceAssemblies(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
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

    static string ProbeRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, ".."));
}
