namespace ABox.Domain.Agents;

// Box config for a sandboxed provider (ADR 0013). The subscription credential rides
// per-turn as SetupToken (the owner's `claude setup-token`), written to a 0600 file on the
// /session mount and read into CLAUDE_CODE_OAUTH_TOKEN inside the box at exec time — never
// baked into the image, never on the PTY-driven exec line, and never in the container's
// `docker inspect` env. It is only leak-safe because egress is confined, so a credentialed box MUST run
// on the egress sidecar's network + proxy (EnsureCredentialConfined). OnboardingHome is a
// non-secret onboarding home (theme / onboarding-complete) copied in per turn so claude
// skips its first-run dialogs; no credential ever lives there.
public sealed record SandboxSettings(
    string Image,
    string? Network = null,
    string? ProxyUrl = null,
    string? SetupToken = null,
    DirectoryInfo? OnboardingHome = null)
{
    // ADR 0013 decision 4: the per-turn token is safe only behind the egress boundary —
    // a box holding the credential must have no default route out. Refuse to open one on
    // docker's default bridge; fail-closed beats a silent exfil channel.
    public void EnsureCredentialConfined()
    {
        if (string.IsNullOrEmpty(SetupToken)) return;
        if (Network is null || ProxyUrl is null)
            throw new InvalidOperationException(
                "A credentialed box must run confined behind the egress sidecar (ADR 0013): set " +
                "Network + ProxyUrl to the egress proxy, or clear SetupToken for an unbilled turn.");
    }
}
