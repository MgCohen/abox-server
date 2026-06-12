namespace RemoteAgents.Tests.Harness;

// The one fact that is infrastructure, not a domain validation: the per-type ParityGuard assertion itself.
// It carries no [Rule] and is the sole exemption from the "every test cites a Rule" completeness check.
public sealed class ParityFact : FactAttribute;
