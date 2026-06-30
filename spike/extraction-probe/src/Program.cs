using System.Runtime.CompilerServices;
using Probe.Domain;

namespace Probe;

// The emit tool.
//   dotnet run -- live    lower the recipe into the throwaway preview dir (live/)
//   dotnet run -- emit     explicit gate: materialize the owned handler at emitted/
//   dotnet run -- prove    live -> emit -> manual-edit -> re-emit, with self-checks
//
// The recipe is REAL CODE: building AuthoredRecipe.AddItemToCart() here means the
// compiler already type-checked the glue. The emitter reads the recipe's identity off
// the composed Mutation<TAgg,TCmd> and LIFTS the glue bodies from the recipe source.
//
// Paths anchored on THIS source file via CallerFilePath so output never escapes the
// folder, never leaks to the repo-root artifacts/ dir.
static class Program
{
    static int Main(string[] args)
    {
        var root = ProbeRoot();
        var mutation = AuthoredRecipe.AddItemToCart();
        var id = new Emitter.Identity(mutation.AggregateType.Name, mutation.CommandType.Name);
        var glue = Lift.From(File.ReadAllText(Path.Combine(root, "src", "AuthoredRecipe.cs")));
        var domain = DomainSource(root);
        var emitTarget = Path.Combine(root, "emitted");
        var previewDir = Path.Combine(root, "live");

        var command = args.Length > 0 ? args[0] : "emit";
        return command switch
        {
            "live" => Live(id, glue, domain, previewDir),
            "emit" => Emit(id, glue, domain, emitTarget),
            "prove" => Prove(id, glue, domain, emitTarget, previewDir),
            _ => Fail($"unknown command '{command}'. Use: live | emit | prove"),
        };
    }

    static int Live(Emitter.Identity id, Lift.Glue glue, string domain, string previewDir)
    {
        Directory.CreateDirectory(previewDir);
        Console.WriteLine($"LIVE  {id.FeatureName}  ->  preview '{Rel(previewDir)}'");
        foreach (var a in Emitter.Lower(id, glue, domain))
        {
            File.WriteAllText(Path.Combine(previewDir, a.RelativePath), a.Text);
            Console.WriteLine($"      regenerated {a.RelativePath}");
        }
        Console.WriteLine("      (preview is overwritten wholesale each run; never hand-edited)");
        return 0;
    }

    static int Emit(Emitter.Identity id, Lift.Glue glue, string domain, string target)
    {
        Directory.CreateDirectory(target);
        Console.WriteLine($"EMIT  {id.FeatureName}  ->  target '{Rel(target)}' (namespace {id.Namespace})");
        foreach (var a in Emitter.Lower(id, glue, domain))
        {
            File.WriteAllText(Path.Combine(target, a.RelativePath), a.Text);
            Console.WriteLine($"      wrote {a.RelativePath}");
        }
        return 0;
    }

    static int Prove(Emitter.Identity id, Lift.Glue glue, string domain, string target, string previewDir)
    {
        var ok = true;
        var handlerFile = Path.Combine(target, $"{id.FeatureName}.Handler.cs");

        Section("0. clean slate");
        if (Directory.Exists(previewDir)) Directory.Delete(previewDir, true);
        if (Directory.Exists(target)) Directory.Delete(target, true);
        Console.WriteLine("      removed live/ and emitted/");

        Section("1. LIVE (preview only — no leak across the gate)");
        Live(id, glue, domain, previewDir);
        ok &= Check(!Directory.Exists(target), "live did NOT create the owned target");

        Section("2. EMIT (explicit gate -> owned handler)");
        Emit(id, glue, domain, target);
        var emitted = File.ReadAllText(handlerFile);
        ok &= Check(File.Exists(handlerFile), "emit created the owned handler file");

        Section("3. the SCAFFOLD was derived from the types (author never wrote it)");
        ok &= Check(emitted.Contains("var __repo = scope.Get<Repo<User>>();"),
            "scaffold: load the aggregate from Repo<User> (derived from TAgg)");
        ok &= Check(emitted.Contains("__repo.Save();"), "scaffold: save (derived)");
        ok &= Check(emitted.Contains("return agg;"), "scaffold: return the aggregate (derived)");

        Section("4. the GLUE was LIFTED from the author's real lambdas, and RENAMED");
        ok &= Check(emitted.Contains("var agg = __repo.Load(command.Email);"),
            "lift: LoadBy(cmd => cmd.Email) -> command.Email, spliced into the load");
        ok &= Check(emitted.Contains("var book = scope.Ask<BookDetails>(new BookDetailsQuery(command.BookId));"),
            "lift: the divergence's first statement, renamed ctx->scope / cmd->command");
        ok &= Check(emitted.Contains("agg.AddToCart(new CartItem(command.BookId, command.Qty, book.Price, book.Label));"),
            "lift: the divergence's second statement, renamed cart->agg / cmd->command");
        ok &= Check(!emitted.Contains("cart.") && !emitted.Contains("cmd.") && !emitted.Contains("ctx."),
            "rename: NONE of the author's natural names (cart/cmd/ctx) leaked into the owned code");

        Section("5. WIRING (probe B) discovered from the assembled body");
        ok &= Check(emitted.Contains("EnsureService<Repo<User>>"),
            "wiring: Repo<User> registered — from the SCAFFOLD's own scope.Get, not authored");
        ok &= Check(emitted.Contains("EnsureQuery<BookDetails>"),
            "wiring: BookDetails query registered — from the LIFTED glue's scope.Ask");

        Section("6. DETACHMENT — live after emit does not touch the owned file");
        Live(id, glue, domain, previewDir);
        ok &= Check(File.ReadAllText(handlerFile) == emitted, "emitted target untouched by a live preview");

        Section("7. OVERRIDE — a manual edit to the emitted file is clobbered by re-emit");
        File.WriteAllText(handlerFile, emitted + "\n// MANUAL SCRIBBLE\n");
        Emit(id, glue, domain, target);
        ok &= Check(!File.ReadAllText(handlerFile).Contains("MANUAL SCRIBBLE"),
            "re-emit overrode the manual edit (re-emit is authoritative)");
        ok &= Check(File.ReadAllText(handlerFile) == emitted, "re-emit is deterministic — byte-identical to the first emit");

        Console.WriteLine();
        Console.WriteLine(ok ? "PROVE: PASS" : "PROVE: FAIL");
        return ok ? 0 : 1;
    }

    static string DomainSource(string root)
    {
        var domain = File.ReadAllText(Path.Combine(root, "src", "Domain.cs"));
        var minted = File.ReadAllText(Path.Combine(root, "src", "Minted.cs"));
        return domain + "\n" + minted;
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

    static string Rel(string path)
    {
        var idx = path.IndexOf("extraction-probe", StringComparison.Ordinal);
        return idx >= 0 ? path[idx..] : path;
    }
}
