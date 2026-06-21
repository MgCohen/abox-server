namespace ABox.Domain.Agents;

// Box configuration for a provider that runs its agent in a sandbox (ADR 0013): the
// image to launch, the egress network to attach + the allowlist proxy URL to set as the
// box's HTTPS_PROXY (both null ⇒ docker default route, no egress hardening; set them to
// the egress-up.sh sidecar to confine the box), and the pre-onboarded template HOME
// (claude config + the owner's credential) copied into each per-turn box. TemplateHome
// is null until the owner provisions a setup-token home — the deferred credential step
// that gates a real billed turn.
public sealed record SandboxSettings(
    string Image,
    string? Network = null,
    DirectoryInfo? TemplateHome = null,
    string? ProxyUrl = null);
