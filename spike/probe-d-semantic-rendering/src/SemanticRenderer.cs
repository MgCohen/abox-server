using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ProbeD;

// PROBE D — the naming fix. The core spike renders types via reflection `Type.FullName`, which
// gives `System.Int32`, backtick-mangled generics (`List`1`), and `Outer+Inner` for nested types,
// forcing a hand-maintained alias dictionary (int->System.Int32, ...) that was dropped as a mess.
//
// The claim: render via Roslyn `ITypeSymbol.ToDisplayString(SymbolDisplayFormat)` instead and
// correct rendering is FREE, in both an idiomatic style and a fully-qualified style, AND the
// `using` set falls out of the symbols. No alias dictionary, no backtick surgery, no `+`->`.`.
//
// `supportTypes` is C# source the renderer compiles alongside the type refs so that user-defined
// types (the nested `Outer.Inner`) resolve to real symbols — the same trick probe-a uses to turn
// `typeof(Guid)` into a symbol via the semantic model.
sealed class SemanticRenderer
{
    readonly CSharpCompilation _compilation;

    SemanticRenderer(CSharpCompilation compilation) => _compilation = compilation;

    public static SemanticRenderer Create(string supportTypes)
    {
        var refs = Net.ReferenceAssemblies();
        var support = CSharpSyntaxTree.ParseText(
            supportTypes,
            new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "ProbeD.Resolution",
            new[] { support },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        return new SemanticRenderer(compilation);
    }

    // Resolve a recipe TypeRef to a real ITypeSymbol by parsing it as the type of a field in a
    // throwaway type and asking the semantic model. This handles every case uniformly — alias,
    // nested, generic, nullable (value + reference), tuple — because it is just "what type is this
    // C# type-expression", answered by the compiler, not by us.
    public ITypeSymbol Resolve(TypeRef type)
    {
        var probe = CSharpSyntaxTree.ParseText(
            $"#nullable enable\nclass __Probe__ {{ {type.Text} __field__; }}",
            new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = _compilation.AddSyntaxTrees(probe);
        var model = compilation.GetSemanticModel(probe);
        var root = probe.GetRoot();
        var fieldType = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclarationSyntax>()
            .Single()
            .Type;
        var symbol = model.GetTypeInfo(fieldType).Type
            ?? throw new InvalidOperationException(
                $"could not resolve type '{type.Text}'. Check the type text or add the user type to support source.");
        if (symbol.TypeKind == TypeKind.Error)
            throw new InvalidOperationException(
                $"type '{type.Text}' did not resolve to a real type (TypeKind.Error). " +
                "If it is a user-defined type, include it in the support source.");

        // Nullable REFERENCE types are a known Roslyn gotcha: GetTypeInfo on a field-type expression
        // surfaces the symbol with NullableAnnotation.None even when the syntax wrote `string?`, so the
        // `?` is lost when rendered. The annotation IS recoverable from the syntax — a `string?` is a
        // NullableTypeSyntax over a reference type — so we re-apply it explicitly. (Nullable VALUE types
        // like `int?` are a distinct Nullable<T> symbol and are unaffected.)
        if (fieldType is Microsoft.CodeAnalysis.CSharp.Syntax.NullableTypeSyntax
            && symbol.IsReferenceType
            && symbol.NullableAnnotation != NullableAnnotation.Annotated)
        {
            return symbol.WithNullableAnnotation(NullableAnnotation.Annotated);
        }
        return symbol;
    }

    // STYLE 1 — idiomatic: int, List<string>, Outer.Inner, string?, (int Count, string Name).
    // UseSpecialTypes => keyword aliases (int, string, ...). MinimallyQualified-style nested name
    // gives `Outer.Inner` (a `.`, never the reflection `+`). Nullable + tuple come out as written.
    public static readonly SymbolDisplayFormat Idiomatic = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    // STYLE 2 — fully-qualified: global::System.Int32, global::System.Collections.Generic.List<...>.
    // No usings needed (and the renderer derives none). FullyQualifiedFormat already does global::
    // + full namespaces; we add the nullable-ref modifier so `string?` stays correct there too.
    public static readonly SymbolDisplayFormat FullyQualified =
        SymbolDisplayFormat.FullyQualifiedFormat
            .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static string RenderIdiomatic(ITypeSymbol type) => type.ToDisplayString(Idiomatic);

    public static string RenderFullyQualified(ITypeSymbol type) => type.ToDisplayString(FullyQualified);

    // Derive the `using` set from the SYMBOLS, not from string parsing: walk every type in the
    // field (the type itself + all generic type arguments + tuple element types, recursively) and
    // collect the containing namespace of each named type. This is the thing reflection FullName
    // could never give cleanly. Special types (int/string) and tuples contribute their namespaces
    // (System / System) but render without a qualifier in idiomatic style, so a `using System;`
    // covering them is harmless and correct.
    public static ImmutableSortedSet<string> DeriveUsings(IEnumerable<ITypeSymbol> fieldTypes)
    {
        var namespaces = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var type in fieldTypes)
            CollectNamespaces(type, namespaces);
        return namespaces.ToImmutable();
    }

    static void CollectNamespaces(ITypeSymbol type, ImmutableSortedSet<string>.Builder into)
    {
        switch (type)
        {
            case INamedTypeSymbol named:
                // A tuple is an INamedTypeSymbol over ValueTuple; recurse into its elements so the
                // element types' namespaces are captured (and skip ValueTuple's own qualifier need
                // since `(int Count, string Name)` renders as a tuple, not ValueTuple<...>).
                if (named.IsTupleType)
                {
                    foreach (var element in named.TupleElements)
                        CollectNamespaces(element.Type, into);
                    return;
                }
                AddNamespace(named, into);
                foreach (var arg in named.TypeArguments)
                    CollectNamespaces(arg, into);
                break;
            case IArrayTypeSymbol array:
                CollectNamespaces(array.ElementType, into);
                break;
            // INamedTypeSymbol covers nullable-value (Nullable<int> -> System) and nested types
            // (containing namespace is the OUTER type's namespace). Nothing else needs a branch
            // for the probe's cases.
        }
    }

    static void AddNamespace(INamedTypeSymbol named, ImmutableSortedSet<string>.Builder into)
    {
        // For a nested type, the namespace is on the outermost containing type.
        var ns = named.ContainingNamespace;
        if (ns is { IsGlobalNamespace: false })
            into.Add(ns.ToDisplayString());
    }
}
