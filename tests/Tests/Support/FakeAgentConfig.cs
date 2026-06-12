using ABox.Domain.Agents;

namespace ABox.Tests.Support;

// No-CLI agent config for tests: carries a scripted Reply the FakeProvider echoes back.
internal sealed record FakeAgentConfig(string Name, string Description, string Model, string SystemPrompt, string? Reply = null)
    : AgentConfig(Name, Description, Model, SystemPrompt);
