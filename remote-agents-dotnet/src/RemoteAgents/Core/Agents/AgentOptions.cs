namespace RemoteAgents.Agents;

// Shared base for the per-provider options records. Owns the trio every
// provider needs (Model / SystemPrompt / Hooks) so the Agent base can
// read them generically — that's what lets UnattendedDirective.Compose
// and hook resolution live in one place instead of duplicated across
// every concrete agent.
//
// Providers add their own fields by inheriting; see ClaudeAgentOptions
// (PTY-timing knobs, permission mode) and CodexAgentOptions
// (sandbox policy, JSON stream timeout).
public abstract record AgentOptions(
    string? Model = null,
    string? SystemPrompt = null,
    HookIntegrationOptions? Hooks = null);
