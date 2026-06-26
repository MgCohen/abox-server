using ABox.Domain.Agents.Codex;

namespace ABox.Host;

// The confined codex box (ADR 0013 + ADR 0007), shared by Composition and the Live smoke tests
// so the two can't drift. Codex runs `codex exec` inside abox-codex on the egress sidecar's
// internal network; its subscription credential is the host ChatGPT-login auth.json, copied per
// provider from the template into the box HOME. The box is always credentialed, so it always
// runs confined behind the proxy (matching the egress-up.sh sidecar names).
internal static class CodexBox
{
    public static CodexSandbox Confined() => new(
        Image: "abox-codex:latest",
        Network: "abox-boxnet",
        ProxyUrl: "http://abox-egress-proxy:8888",
        AuthTemplate: CodexHome.MaterializeTemplate());
}
