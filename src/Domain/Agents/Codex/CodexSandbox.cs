namespace ABox.Domain.Agents.Codex;

// Box config for the codex provider (ADR 0013 + ADR 0007). Codex runs `codex exec` inside a
// per-turn confined box over a non-PTY `docker exec -i` pipe. Unlike claude there is no
// SetupToken channel: codex's subscription credential is the ChatGPT-login `auth.json`, copied
// per provider into AuthTemplate's `.codex/` and mounted as the box HOME. The box always holds
// that credential, so it always runs confined — Network + ProxyUrl are required, never null.
public sealed record CodexSandbox(
    string Image,
    string Network,
    string ProxyUrl,
    DirectoryInfo AuthTemplate);
