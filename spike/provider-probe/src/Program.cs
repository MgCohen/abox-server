using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Probe;

// The probe.
//   dotnet run -- emit    emit both owned handlers (Repo + Bucket) from the two recipes
//   dotnet run -- prove   emit + assert the two outputs + the compiler REJECTS a bad swap
//
// The two recipes differ by ONE token (via) plus the key it forces. Both type-check;
// the emitter lowers each to a different owned handler; and a mismatched key does not
// compile (the negative check). Paths anchored via CallerFilePath.
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
            RepoRecipe.AddPoints(),
            Lift.From(File.ReadAllText(Path.Combine(root, "src", "RepoRecipe.cs")), "AddPoints"));
        var bucket = Emitter.Lower(
            BucketRecipe.AddPoints(),
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

        Section("2. SAME shape + SAME output — the body is provider-agnostic");
        ok &= Check(repo.Text.Contains("agg.AddPoints(command.Points);")
                 && bucket.Text.Contains("agg.AddPoints(command.Points);"),
            "identical lifted body in both handlers");
        ok &= Check(repo.Text.Contains("return agg;") && bucket.Text.Contains("return agg;"),
            "both return the aggregate (same output type: User)");

        Section("3. THE TYPE-SAFE SWAP — the compiler REJECTS a key that doesn't match the provider");
        var shared = new[] { "Store.cs", "Domain.cs", "Feature.cs" }
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

    // A recipe that plugs a Bucket provider but takes `key` as a parameter, so we can vary
    // it. With key => BucketKey it must compile; with key => string it must NOT.
    static string Snippet(string key) => $$"""
        using Probe;
        using Probe.Domain;
        public static class __Neg
        {
            public static Mutation Recipe() => Feature.For<AddPointsCommand>().Mutate(
                via:  Stores.BucketStore<User>(),
                key:  {{key}},
                body: (user, c) => user.AddPoints(c.Points));
        }
        """;

    static IReadOnlyList<Diagnostic> Errors(string[] sharedSources, string snippet)
    {
        var options = new CSharpParseOptions(LanguageVersion.Latest);
        // The project builds with ImplicitUsings; the throwaway compilation must supply
        // the same global usings or the shared sources' Dictionary<,> etc. won't resolve.
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
