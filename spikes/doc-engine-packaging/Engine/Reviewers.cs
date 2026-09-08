namespace ABox.DocEngine;

public static class Reviewers
{
    // Every doc is judged against its docType's rubric by default; a docType opts into extra reviewers
    // (or out, with an explicit empty list) via its `reviewers:` field. The judge is the universal.
    public static readonly IReadOnlyList<string> Default = new[] { "judge" };

    public static IReadOnlyList<string> Resolve(IReadOnlyDictionary<string, object?> doctype) =>
        doctype.ContainsKey("reviewers")
            ? Yaml.AsList(doctype["reviewers"]).OfType<string>().ToList()
            : Default;
}
