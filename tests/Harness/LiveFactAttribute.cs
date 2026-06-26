namespace ABox.Tests.Harness;

// The live-CLI gate: a Fact that runs only when RUN_LIVE=1. Default `dotnet test` (and CI) skips it with a
// clear reason; set the env var to run the real claude/codex suites. Replaces the hand-typed Skip const, so
// live tests run by setting an env var, never by editing source.
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_LIVE") != "1")
            Skip = "live: needs the real claude/codex CLI + subscription. Set RUN_LIVE=1 to run.";
    }
}
