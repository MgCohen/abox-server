namespace ABox.DocEngine;

public static class Checks
{
    // Custom deterministic checks a docType opts into (scripts, engine-relative). They run after the
    // generic `validate` and, like it, BLOCK on failure — the cheap, objective, per-docType tier that
    // the structural validator can't express and an agent reviewer is the wrong tool for. None by default.
    public static IReadOnlyList<string> Resolve(IReadOnlyDictionary<string, object?> doctype) =>
        Yaml.AsList(doctype.GetValueOrDefault("checks")).OfType<string>().ToList();
}
