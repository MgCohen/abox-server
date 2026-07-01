namespace Spike;

// Marks a method as a snippet. The string is the catalog key the recipe nodes resolve to.
[AttributeUsage(AttributeTargets.Method)]
sealed class SnippetAttribute(string key) : Attribute
{
    public string Key { get; } = key;
}

// The body-hole marker. `Slot.Of<T>()` is real, compiling, type-checked C# at authoring
// time; the generator replaces the call with rendered child code. It is never executed.
static class Slot
{
    public static T Of<T>() =>
        throw new InvalidOperationException("Slot is a compile-time placeholder; it is never executed.");
}
