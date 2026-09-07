namespace Kit.Tests.Harness;

// The renamed seam. tests/Harness/RepoTree hardcodes `private const string Marker = "ABox.slnx"`; the extraction
// turns that one token into a parameter so a new home names its own root marker. Everything else about locating
// the root — walk up from the base dir, throw loud if not found so a broken scan can't go vacuously green — is
// the original logic.
public static class RepoRoot
{
    public static string LocateBy(string marker, string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, marker)))
                return dir.FullName;

        throw new InvalidOperationException(
            $"Could not locate the repo root: no '{marker}' found walking up from '{start}'. " +
            "The structure guards would be vacuously green — fix the marker or the locator.");
    }
}
