namespace Spike;

// The declaration tier: the element that HOLDS a body, not the body itself. These are structural
// nodes, hand-authored like Block/Var/Lit — NOT snippet-generated (snippets model the constructs
// that go inside a body; a type declaration is the container). Record first; class/struct/enum are
// the variations that validate the model across the four basic type kinds.

// A reference to a type BY NAME — because a field's type is often one a sibling recipe is still
// generating (a Service's IRepository), which has no CLR type to point at. Of<T> is sugar for the
// real-type case; the string form names anything, including a not-yet-generated type.
readonly record struct TypeRef(string Name)
{
    public static implicit operator TypeRef(string name) => new(name);

    public static TypeRef Of<T>() => new(Keywords.GetValueOrDefault(typeof(T), typeof(T).Name));

    public override string ToString() => Name;

    static readonly Dictionary<Type, string> Keywords = new()
    {
        [typeof(string)] = "string",
        [typeof(bool)] = "bool",
        [typeof(int)] = "int",
        [typeof(long)] = "long",
        [typeof(double)] = "double",
        [typeof(decimal)] = "decimal",
        [typeof(object)] = "object",
    };
}

record Field(string Name, TypeRef Type);

sealed record Field<T>(string Name) : Field(Name, TypeRef.Of<T>());

abstract record TypeDecl(string Name);

sealed record RecordNode(string Name, params Field[] Members) : TypeDecl(Name);

sealed record ClassNode(string Name, params Field[] Members) : TypeDecl(Name);

sealed record StructNode(string Name, params Field[] Members) : TypeDecl(Name);

sealed record EnumNode(string Name, params string[] Members) : TypeDecl(Name);
