using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Slice;

// The shared symbol table all four emit passes resolve against — the keystone of
// the integration. It is ONE Compilation built from:
//   - the catalog support source (User, Repo<T>, BookDetails, ... + the runtime
//     Scope so scope.Get<T> markers type-check during the wiring scan),
//   - the minted models lowered to record source FIRST (probe A as data),
//     declared in the feature namespace so the glue's return type and field
//     types can name them (probe E, same-compilation forward ref).
//
// Because mint feeds this model, render/wire/glue all see the minted types as
// real symbols — that is the composition the isolated probes never had to make.
sealed class ResolutionModel
{
    readonly CSharpCompilation _compilation;
    readonly string _featureNamespace;

    ResolutionModel(CSharpCompilation compilation, string featureNamespace)
    {
        _compilation = compilation;
        _featureNamespace = featureNamespace;
    }

    public static ResolutionModel Build(string catalogSource, string mintedModelsSource, string featureNamespace)
    {
        var options = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(catalogSource, options),
            CSharpSyntaxTree.ParseText(mintedModelsSource, options),
        };
        var compilation = CSharpCompilation.Create(
            "Slice.Resolution",
            trees,
            Net.ReferenceAssemblies(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        return new ResolutionModel(compilation, featureNamespace);
    }

    // Resolve a recipe TypeRef to a real ITypeSymbol by parsing it as a field
    // type in a throwaway class and asking the semantic model (probe D's trick).
    // Works uniformly for BCL, generic, nested, nullable — AND for a minted model
    // name, because the minted source is in this compilation (probe E).
    public ITypeSymbol Resolve(TypeRef type)
    {
        // The author writes idiomatic short names ("Guid", "Repo<User>"), not
        // fully-qualified ones. We open the namespaces those names live in so the
        // probe resolves them the way the author means — System for the BCL, the
        // catalog/runtime/feature namespaces for catalog and minted types. (A real
        // emit would derive this open-set from the recipe's catalog config rather
        // than hardcode it — friction the isolated probes never hit, see README.)
        var probe = CSharpSyntaxTree.ParseText(
            "#nullable enable\nusing System;\nusing System.Collections.Generic;\n" +
            $"using Slice.Catalog;\nusing Slice.Runtime;\n" +
            $"namespace {_featureNamespace} {{ class __Probe__ {{ {type.Text} __field__; }} }}",
            new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = _compilation.AddSyntaxTrees(probe);
        var model = compilation.GetSemanticModel(probe);
        var fieldType = probe.GetRoot()
            .DescendantNodes()
            .OfType<VariableDeclarationSyntax>()
            .Single()
            .Type;
        var symbol = model.GetTypeInfo(fieldType).Type
            ?? throw new InvalidOperationException($"could not resolve type '{type.Text}'.");
        if (symbol.TypeKind == TypeKind.Error)
            throw new InvalidOperationException(
                $"type '{type.Text}' did not resolve (TypeKind.Error). " +
                "If it is a minted model, check it is in the recipe's Models; if a catalog type, add it to the catalog support source.");
        return symbol;
    }

    // STYLE: idiomatic — int, List<string>, Repo<User>, CartItem — with a derived
    // using set walked off the symbol graph (probe D). The slice emits idiomatic
    // (readable owned code), not fully-qualified.
    public static readonly SymbolDisplayFormat Idiomatic = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static string Render(ITypeSymbol type) => type.ToDisplayString(Idiomatic);

    // Walk a symbol (itself + generic args + array element, recursively) and
    // collect containing namespaces, so the emitted file's usings fall out of the
    // symbols, not string parsing (probe D). `skipNamespace` drops the feature's
    // own namespace — a minted model in CartItem's own file needs no using.
    public static IReadOnlyCollection<string> DeriveUsings(IEnumerable<ITypeSymbol> types, string skipNamespace)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var t in types)
            Collect(t, set);
        set.Remove(skipNamespace);
        return set;
    }

    static void Collect(ITypeSymbol type, SortedSet<string> into)
    {
        switch (type)
        {
            case INamedTypeSymbol named:
                if (named.IsTupleType)
                {
                    foreach (var e in named.TupleElements)
                        Collect(e.Type, into);
                    return;
                }
                var ns = named.ContainingNamespace;
                if (ns is { IsGlobalNamespace: false })
                    into.Add(ns.ToDisplayString());
                foreach (var arg in named.TypeArguments)
                    Collect(arg, into);
                break;
            case IArrayTypeSymbol array:
                Collect(array.ElementType, into);
                break;
        }
    }
}
