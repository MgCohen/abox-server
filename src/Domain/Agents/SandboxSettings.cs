namespace ABox.Domain.Agents;

// Box config for a sandboxed provider (ADR 0013). Network + ProxyUrl null ⇒ docker's
// default route with no egress hardening; point them at the egress-up.sh sidecar to
// confine the box. TemplateHome is the pre-onboarded credential home copied in per turn.
public sealed record SandboxSettings(
    string Image,
    string? Network = null,
    DirectoryInfo? TemplateHome = null,
    string? ProxyUrl = null);
