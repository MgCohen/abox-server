namespace Probe;

// A terse model declaration parser — written ONCE, reused. Replaces the slice's
// field-by-field `new ModelSpec("CartItem", [ new FieldSpec("BookId", "Guid"), ...
// ])` ceremony with the one-line shape an author would actually scribble:
//
//   "CartItem(BookId:Guid, Qty:int, Price:decimal, Label:string)"
//
// Each field is "name:Type"; the Type is the same idiomatic C# type text the
// renderer (probe D) resolves. Multiple models = one declaration per line.
sealed record TerseModel(string Name, IReadOnlyList<TerseModel.Field> Fields)
{
    public readonly record struct Field(string Name, string Type);

    public static TerseModel Parse(string declaration)
    {
        var text = declaration.Trim();
        var open = text.IndexOf('(');
        var close = text.LastIndexOf(')');
        if (open < 0 || close < open)
            throw new FormatException(
                $"terse model '{declaration}' must be 'Name(field:Type, ...)'.");

        var name = text[..open].Trim();
        var inner = text[(open + 1)..close].Trim();
        var fields = inner.Length == 0
            ? new List<Field>()
            : inner.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseField)
                .ToList();
        return new TerseModel(name, fields);
    }

    public static IReadOnlyList<TerseModel> ParseAll(string declarations) =>
        declarations
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(Parse)
            .ToList();

    static Field ParseField(string spec)
    {
        var parts = spec.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            throw new FormatException($"terse field '{spec}' must be 'name:Type'.");
        return new Field(parts[0], parts[1]);
    }
}
