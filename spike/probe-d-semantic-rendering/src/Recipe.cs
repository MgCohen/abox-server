namespace ProbeD;

// The AUTHOR-FACING recipe shape. This is what an agent composes — a typed tree of records,
// exactly the shape the rest of the spike already uses (cf. probe-c's Recipe / the README's
// `record RecordNode(...)`). A type is a name + an ordered list of fields; a field is a name +
// the type it holds. The type is named the way the author already names types in the spike: as
// C# type text. NOTHING here mentions Roslyn, ITypeSymbol, or SymbolDisplayFormat — the semantic
// model lives entirely behind the renderer. That separation is the meta-finding for the lead.
sealed record RecordRecipe(string Name, IReadOnlyList<Field> Fields);

// A field: a name and a TypeRef. `TypeRef` is the spike's existing notion (README §8 #17) — a
// type named at the value level rather than as a generic `<T>`, because a recipe must be able to
// name types it does not have a CLR `Type` for (nullable value, tuple, a sibling-generated type).
sealed record Field(string Name, TypeRef Type);

// A reference to a type, authored as the C# type text the author would write inline: "int",
// "string?", "List<int>", "Outer.Inner", "(int Count, string Name)". The renderer resolves this
// to a real ITypeSymbol and renders it — the author never sees the resolution step.
sealed record TypeRef(string Text)
{
    public static implicit operator TypeRef(string text) => new(text);
}
