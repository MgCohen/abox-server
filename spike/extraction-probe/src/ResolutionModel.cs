using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Probe;

// The shared symbol table the emit passes resolve against (reused infra from the
// earlier probes). ONE Compilation over the domain support source, so render/wire all
// see the domain + minted types as real symbols. Type text -> ITypeSymbol -> idiomatic
// rendering (probe D).
sealed class ResolutionModel
{
    readonly CSharpCompilation _compilation;
    readonly string _featureNamespace;

    ResolutionModel(CSharpCompilation compilation, string featureNamespace)
    {
        _compilation = compilation;
        _featureNamespace = featureNamespace;
    }

    public static ResolutionModel Build(string domainSource, string featureNamespace)
    {
        var options = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = new[] { CSharpSyntaxTree.ParseText(domainSource, options) };
        var compilation = CSharpCompilation.Create(
            "Probe.Resolution",
            trees,
            Net.ReferenceAssemblies(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        return new ResolutionModel(compilation, featureNamespace);
    }

    public ITypeSymbol Resolve(string typeText)
    {
        var probe = CSharpSyntaxTree.ParseText(
            "#nullable enable\nusing System;\nusing System.Collections.Generic;\n" +
            "using Probe.Domain;\n" +
            $"namespace {_featureNamespace} {{ class __Probe__ {{ {typeText} __field__; }} }}",
            new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = _compilation.AddSyntaxTrees(probe);
        var model = compilation.GetSemanticModel(probe);
        var fieldType = probe.GetRoot()
            .DescendantNodes()
            .OfType<VariableDeclarationSyntax>()
            .Single()
            .Type;
        var symbol = model.GetTypeInfo(fieldType).Type
            ?? throw new InvalidOperationException($"could not resolve type '{typeText}'.");
        if (symbol.TypeKind == TypeKind.Error)
            throw new InvalidOperationException(
                $"type '{typeText}' did not resolve (TypeKind.Error). " +
                "If it is a minted model, check Minted.cs; if a domain type, check Domain.cs.");
        return symbol;
    }

    public static readonly SymbolDisplayFormat Idiomatic = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static string Render(ITypeSymbol type) => type.ToDisplayString(Idiomatic);

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
