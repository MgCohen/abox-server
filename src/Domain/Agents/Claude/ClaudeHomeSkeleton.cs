namespace ABox.Domain.Agents.Claude;

// Provisional (B1/B2): a non-secret first-run HOME seeded into each per-turn box so claude
// reaches its input prompt instead of stalling on first-run dialogs. Onboarding/theme are
// pre-completed, and bypass-permissions is pre-accepted (the box IS the sandbox that warning
// asks about) — racing keystrokes to dismiss it confirmed the "No, exit" default ~1 in 3.
// No credential lives here — the subscription token rides the box env per turn. Keys verified
// against claude-code 2.1.187 and are version-fragile.
public static class ClaudeHomeSkeleton
{
    private const string FirstRunState =
        """{"hasCompletedOnboarding":true,"numStartups":2,"theme":"dark","bypassPermissionsModeAccepted":true}""";

    public static DirectoryInfo Materialize()
    {
        var dir = Directory.CreateTempSubdirectory("abox-claude-home-skeleton-");
        File.WriteAllText(Path.Combine(dir.FullName, ".claude.json"), FirstRunState);
        return dir;
    }
}
