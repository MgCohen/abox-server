using static ABox.Tests.Harness.Report;

namespace ABox.Tests.Meta.Tests;

// The artifact registry stays well-formed: every governance/registry/<Name>/artifact.yml declares the
// agent-first floor — a purpose (when to reach for it), an existing home, a known family, and a gate. Read from
// disk (Artifacts), so a newly registered artifact is covered the moment its folder lands. Template + criteria
// stay the per-type Rulebook guards' job; this owns the registry floor.
public class ArtifactTests
{
    private static readonly string[] Families = { "code-first", "nl-first" };
    private static readonly string[] Gates = { "block", "advise" };

    [Rule("Every artifact declares the floor")]
    [Fact]
    public void EveryArtifactDeclaresTheFloor()
    {
        var violations = new List<string>();
        foreach (var artifact in Artifacts.All())
        {
            var f = artifact.Fields;

            if (!f.TryGetValue("purpose", out var purpose) || purpose.Length == 0)
                violations.Add($"{artifact.Name}: no 'purpose' — an agent can't tell when to reach for it");

            if (!f.TryGetValue("home", out var home) || home.Length == 0 ||
                !Directory.Exists(Path.Combine(RepoTree.Root, home)))
                violations.Add($"{artifact.Name}: 'home' missing or not a directory (got '{f.GetValueOrDefault("home", "")}')");

            if (!f.TryGetValue("family", out var family) || !Families.Contains(family))
                violations.Add($"{artifact.Name}: 'family' must be one of [{Join(Families)}] (got '{f.GetValueOrDefault("family", "")}')");

            if (!f.TryGetValue("gate", out var gate) || !Gates.Contains(gate))
                violations.Add($"{artifact.Name}: 'gate' must be one of [{Join(Gates)}] (got '{f.GetValueOrDefault("gate", "")}')");

            // parity is the optional second-representation binding (Q2): a code-first artifact declares a target,
            // an nl-first one has nothing to bind — so the field must be consistent with family, never dead data.
            var hasParity = f.TryGetValue("parity", out var parity) && parity.Length > 0 && parity != "null";
            if (f.GetValueOrDefault("family") == "code-first" && !hasParity)
                violations.Add($"{artifact.Name}: 'code-first' must declare a 'parity' target (the representation it binds to)");
            if (f.GetValueOrDefault("family") == "nl-first" && hasParity)
                violations.Add($"{artifact.Name}: 'nl-first' must not declare 'parity' (no second representation to bind)");
        }

        Assert.True(violations.Count == 0,
            $"""
            Artifact registry entries are missing the floor:
            {Bullets(violations)}
            Every governance/registry/<Name>/artifact.yml must declare purpose + home (existing) + family + gate.
            """);
    }
}
