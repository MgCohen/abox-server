using ABox.Domain.Agents;
using ABox.Domain.Agents.Claude;

namespace ABox.Host;

// One place for the confined agent box (ADR 0013): the egress-up.sh sidecar names plus the
// per-turn credential wiring, shared by Composition and the Live smoke tests so the two can't
// drift. The subscription token is host-held and read from the environment so it never lives in
// source; null until provisioned, which gates a real billed turn (B1/B2). EnsureCredentialConfined
// forbids a credentialed box on the default bridge, so the token only ships alongside the network.
internal static class ClaudeBox
{
    public static SandboxSettings Confined() => new(
        Image: "abox-claude:latest",
        Network: "abox-boxnet",
        ProxyUrl: "http://abox-egress-proxy:8888",
        SetupToken: Environment.GetEnvironmentVariable("ABOX_CLAUDE_SETUP_TOKEN"),
        HomeSkeleton: ClaudeHomeSkeleton.Materialize());
}
