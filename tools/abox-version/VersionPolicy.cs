namespace ABox.Versioning;

public sealed record VersionBump(SemVer Next, string Level);

public static class VersionPolicy
{
    // Pre-1.0 (Major==0): major is frozen at 0 ("not API-stable"), so a breaking change lands in minor
    // and an addition in patch. Post-1.0: standard compatibility semver. The owner cuts 1.0.0 by hand;
    // the same classification then auto-shifts up one notch with no rule change here.
    public static VersionBump? Next(SemVer current, Compat change)
    {
        var preStable = current.Major == 0;
        return change switch
        {
            Compat.Breaking => preStable
                ? new(current with { Minor = current.Minor + 1, Patch = 0 }, "minor")
                : new(current with { Major = current.Major + 1, Minor = 0, Patch = 0 }, "major"),
            Compat.Additive => preStable
                ? new(current with { Patch = current.Patch + 1 }, "patch")
                : new(current with { Minor = current.Minor + 1, Patch = 0 }, "minor"),
            _ => null,
        };
    }
}
