namespace Spike;

// Marks a method as a snippet. The string is the catalog key the recipe nodes resolve to.
[AttributeUsage(AttributeTargets.Method)]
sealed class SnippetAttribute(string key) : Attribute
{
    public string Key { get; } = key;
}
