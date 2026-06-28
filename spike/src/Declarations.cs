namespace Spike;

// The declaration tier: the element that HOLDS a body, not the body itself. These are structural
// nodes, hand-authored like Block/Var/Lit — NOT snippet-generated (snippets model the constructs
// that go inside a body; a type declaration is the container). Record first; class/struct/enum are
// the variations that validate the model across the four basic type kinds.

readonly record struct TypeRef(string Name)
{
    public static implicit operator TypeRef(string name) => new(name);

    public override string ToString() => Name;
}

sealed record Field(string Name, TypeRef Type);

abstract record TypeDecl(string Name);

sealed record RecordNode(string Name, params Field[] Members) : TypeDecl(Name);

sealed record ClassNode(string Name, params Field[] Members) : TypeDecl(Name);

sealed record StructNode(string Name, params Field[] Members) : TypeDecl(Name);

sealed record EnumNode(string Name, params string[] Members) : TypeDecl(Name);
