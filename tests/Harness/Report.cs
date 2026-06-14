namespace ABox.Tests.Harness;

// Shared formatting for the guards' failure messages: a bullet list and a sorted inline join. Promoted here
// once a third guard needed the same shape, so the layout can't drift between them.
public static class Report
{
    public static string Bullets(IEnumerable<string> items) =>
        string.Join(Environment.NewLine, items.Select(i => $"  * {i}"));

    public static string Join(IEnumerable<string> items) =>
        string.Join(", ", items.OrderBy(i => i, StringComparer.Ordinal));
}
