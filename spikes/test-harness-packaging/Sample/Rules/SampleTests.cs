using Kit.Tests.Harness;
using Xunit;

namespace Kit.Sample.Tests.Rules;

// The graded suite: every [Fact] cites a [Rule] whose text matches a '### ' header in Rules/Rulebook.md. This is
// what ParityGuard scopes to (Kit.Sample.Tests.Rules) and holds in lockstep with the Rulebook.
public sealed class SampleTests
{
    [Fact]
    [Rule("a sample guarantee holds")]
    public void Sample_guarantee_is_enforced() => Assert.NotNull(Guid.NewGuid().ToString());

    [Fact]
    [Rule("a second guarantee holds")]
    public void Second_guarantee_is_enforced() => Assert.NotEmpty(new[] { 1, 2 });
}
