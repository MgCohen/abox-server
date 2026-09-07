using System.Reflection;
using Kit.Tests.Harness;
using Xunit;

namespace Kit.Sample.Tests;

// The meta-runner: it drives the extracted engine over the sample suite, so it lives OUTSIDE the parity scope
// (Kit.Sample.Tests, not .Rules) and carries no [Rule] itself — exactly as the harness's own tests sit outside
// the product taxonomy they police.
public sealed class ParityTests
{
    [Fact]
    public void Parity_holds_for_the_sample_type() =>
        ParityGuard.For(typeof(Rules.SampleTests).Assembly, "Rules").Assert();

    [Fact]
    public void Root_is_located_by_a_configurable_marker()
    {
        var root = RepoRoot.LocateBy("Kit.root", AppContext.BaseDirectory);
        Assert.True(File.Exists(Path.Combine(root, "Kit.root")));
    }

    [Fact]
    public void A_missing_marker_fails_loud_rather_than_going_vacuously_green() =>
        Assert.Throws<InvalidOperationException>(
            () => RepoRoot.LocateBy("no-such-marker.xyz", AppContext.BaseDirectory));
}
