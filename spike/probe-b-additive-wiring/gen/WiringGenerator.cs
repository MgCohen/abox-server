using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ProbeB.Gen;

// PROBE B — additive wiring. A classic IIncrementalGenerator (NO interceptors,
// NO [InterceptsLocation]). It SCANS the use-site for capability markers
// `scope.Get<T>()` / `scope.Ask<T>()` and emits a typed manifest + a DI
// registration method ADDITIVELY. The authored lambda body is never edited:
// scope.Get<T>/scope.Ask<T> are real runtime methods that resolve from the
// container (see consumer/Scope.cs), so the body compiles and runs as written,
// AND the generator statically enumerates the deps from the same call sites.
[Generator]
public sealed class WiringGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var handlers = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsHandlerRegistration(node),
                transform: static (ctx, _) => Extract(ctx))
            .Where(static h => h is not null)
            .Select(static (h, _) => h!);

        var collected = handlers.Collect();

        context.RegisterSourceOutput(collected, static (spc, all) => Emit(spc, all));
    }

    // Use-site shape we scan: `Handler<TCommand>(scope => { ... })` /
    // `Endpoint<TCommand>(scope => { ... })`. We key purely on syntax — the
    // marker grammar is in the wiring, exactly the "minimal dialect" invariant.
    static bool IsHandlerRegistration(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax inv)
            return false;
        if (WrapperName(inv) is null)
            return false;
        return inv.ArgumentList.Arguments.Count == 1
            && inv.ArgumentList.Arguments[0].Expression
                is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax;
    }

    // The `Handler<T>` / `Endpoint<T>` name, whether called bare (`Handler<T>(...)`)
    // or qualified (`App.Handler<T>(...)`). Returns the generic name carrying <T>.
    static GenericNameSyntax? WrapperName(InvocationExpressionSyntax inv)
    {
        var name = inv.Expression switch
        {
            GenericNameSyntax g => g,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax g } => g,
            _ => null,
        };
        if (name is null || name.TypeArgumentList.Arguments.Count != 1)
            return null;
        return name.Identifier.ValueText is "Handler" or "Endpoint" ? name : null;
    }

    static HandlerInfo? Extract(GeneratorSyntaxContext ctx)
    {
        var inv = (InvocationExpressionSyntax)ctx.Node;
        var g = WrapperName(inv);
        if (g is null)
            return null;
        var commandType = ctx.SemanticModel.GetTypeInfo(g.TypeArgumentList.Arguments[0]).Type;
        var commandName = commandType?.ToDisplayString() ?? g.TypeArgumentList.Arguments[0].ToString();

        var lambda = (LambdaExpressionSyntax)inv.ArgumentList.Arguments[0].Expression;
        var scopeParam = ScopeParamName(lambda);
        if (scopeParam is null)
            return null;

        var deps = ImmutableArray.CreateBuilder<Dependency>();
        bool sawDynamic = false;

        foreach (var call in lambda.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax ma)
                continue;
            if (ma.Expression is not IdentifierNameSyntax recv || recv.Identifier.ValueText != scopeParam)
                continue;
            if (ma.Name is not GenericNameSyntax verb || verb.TypeArgumentList.Arguments.Count != 1)
            {
                // scope.Get(...) with no type arg, or a non-generic capability call: not
                // statically enumerable into a typed registration. Record the limitation.
                if (ma.Name is IdentifierNameSyntax bare && bare.Identifier.ValueText is "Get" or "Ask")
                    sawDynamic = true;
                continue;
            }
            var kind = verb.Identifier.ValueText switch
            {
                "Get" => DepKind.Service,
                "Ask" => DepKind.Query,
                _ => DepKind.None,
            };
            if (kind == DepKind.None)
                continue;

            var argType = ctx.SemanticModel.GetTypeInfo(verb.TypeArgumentList.Arguments[0]).Type;
            var typeName = argType?.ToDisplayString() ?? verb.TypeArgumentList.Arguments[0].ToString();
            deps.Add(new Dependency(kind, typeName));
        }

        // Distinct (kind,type) — a looped or repeated scope.Get<T>() is one dependency.
        var distinct = deps
            .GroupBy(d => (d.DepKind, d.Type))
            .Select(grp => grp.First())
            .ToImmutableArray();

        return new HandlerInfo(commandName, distinct, sawDynamic);
    }

    static string? ScopeParamName(LambdaExpressionSyntax lambda) => lambda switch
    {
        SimpleLambdaExpressionSyntax s => s.Parameter.Identifier.ValueText,
        ParenthesizedLambdaExpressionSyntax p when p.ParameterList.Parameters.Count == 1
            => p.ParameterList.Parameters[0].Identifier.ValueText,
        _ => null,
    };

    static void Emit(SourceProductionContext spc, ImmutableArray<HandlerInfo> handlers)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/> PROBE B additive wiring — emitted by WiringGenerator.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();
        sb.AppendLine("namespace ProbeB;");
        sb.AppendLine();

        sb.AppendLine("// A typed manifest of every dependency the generator statically found at the");
        sb.AppendLine("// use-sites, plus a registration method that pre-binds them into the container.");
        sb.AppendLine("public static partial class Wiring");
        sb.AppendLine("{");

        // Per-handler manifest: the typed list of deps discovered for that handler.
        sb.AppendLine("    public sealed record Dep(string Kind, string Type);");
        sb.AppendLine("    public sealed record HandlerManifest(string Command, IReadOnlyList<Dep> Dependencies);");
        sb.AppendLine();

        sb.AppendLine("    public static IReadOnlyList<HandlerManifest> Manifest { get; } = new HandlerManifest[]");
        sb.AppendLine("    {");
        foreach (var h in handlers)
        {
            sb.AppendLine($"        new HandlerManifest(\"{h.Command}\", new Dep[]");
            sb.AppendLine("        {");
            foreach (var d in h.Deps)
                sb.AppendLine($"            new Dep(\"{d.Kind}\", \"{Escape(d.Type)}\"),");
            sb.AppendLine("        }),");
        }
        sb.AppendLine("    };");
        sb.AppendLine();

        // Registration: pre-validate that the container can resolve every static dep.
        // Purely additive — it inspects what was scanned, it does not rewrite the body.
        sb.AppendLine("    // Pre-binds / validates every statically-discovered dependency against the");
        sb.AppendLine("    // container. Generated additively from the markers — the lambda is untouched.");
        sb.AppendLine("    public static void RegisterDiscovered(Container c)");
        sb.AppendLine("    {");
        foreach (var h in handlers)
        {
            sb.AppendLine($"        // {h.Command}");
            foreach (var d in h.Deps)
            {
                if (d.Kind == "Service")
                    sb.AppendLine($"        c.EnsureService<{d.Type}>();");
                else
                    sb.AppendLine($"        c.EnsureQuery<{d.Type}>();");
            }
            if (h.SawDynamic)
                sb.AppendLine($"        // NOTE: {h.Command} has a non-generic / dynamic scope.Get/Ask call the generator could not enumerate.");
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("Wiring.g.cs", sb.ToString());
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    enum DepKind { None, Service, Query }

    sealed record Dependency(DepKind DepKind, string Type)
    {
        public string Kind => DepKind.ToString();
    }

    sealed record HandlerInfo(
        string Command,
        ImmutableArray<Dependency> Deps,
        bool SawDynamic);
}
