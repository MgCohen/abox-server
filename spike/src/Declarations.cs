namespace Spike;

// The declaration tier: the element that HOLDS a body, not the body itself. These are structural
// nodes, hand-authored like Block/Var/Lit — NOT snippet-generated (snippets model the constructs
// that go inside a body; a type declaration is the container). Record first; class/struct/enum are
// the variations that validate the model across the four basic type kinds.

// A reference to a type. Of<T> names an EXISTING type — fully-qualified (typeof(T).FullName), so it
// is compiler-checked AND needs no `using`; agent-first output, so the verbose name is fine. The
// string form names a type being CREATED in the recipe (a sibling's IRepository), which has no CLR
// type to point at.
readonly record struct TypeRef(string Name)
{
    public static implicit operator TypeRef(string name) => new(name);

    public static TypeRef Of<T>() => new(typeof(T).FullName ?? typeof(T).Name);

    public override string ToString() => Name;
}

record Field(string Name, TypeRef Type);

sealed record Field<T>(string Name) : Field(Name, TypeRef.Of<T>());

abstract record TypeNode(string Name);

sealed record RecordNode(string Name, params Field[] Members) : TypeNode(Name);

sealed record ClassNode(string Name, params Field[] Members) : TypeNode(Name);

sealed record StructNode(string Name, params Field[] Members) : TypeNode(Name);

sealed record EnumNode(string Name, params string[] Members) : TypeNode(Name);
