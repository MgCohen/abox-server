using RemoteAgents.Agents.Hooks;

namespace RemoteAgents.Agents;

public sealed record CodexAgentOptions(
    string Sandbox = "workspace-write",
    int JsonStreamTimeoutMs = 60_000,
    // gpt-5.5 is what works on a ChatGPT subscription; gpt-5.3-codex (the
    // codex CLI's intrinsic default at present) is API-only and would 400
    // out under subscription billing.
    string? Model = "gpt-5.5",
    string? SystemPrompt = null,
    // Opt-in hook integration. When non-null, the agent installs
    // ~/.codex/hooks.json pointing at ShimPath, sets REMOTEAGENTS_HOOKS_JSONL
    // on the spawned codex process, and resolves AgentResult.Status / Question
    // from the resulting hooks.jsonl. See HookIntegrationOptions.
    HookIntegrationOptions? Hooks = null)
    : AgentOptions(Model, SystemPrompt, Hooks);
