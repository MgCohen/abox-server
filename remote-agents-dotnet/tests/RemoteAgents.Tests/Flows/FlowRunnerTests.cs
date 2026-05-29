using RemoteAgents.Flows;
using RemoteAgents.Sessions;

namespace RemoteAgents.Tests.Flows;

// FlowRunner.MapToExitCode is the single place a run's SessionResult
// becomes a CLI process exit code. Lock the contract so the success /
// gate-failure / error buckets don't drift.
public class FlowRunnerTests
{
    [Theory]
    [InlineData(SessionResult.Shipped, 0)]
    [InlineData(SessionResult.Ok, 0)]
    [InlineData(SessionResult.NoChanges, 0)]
    [InlineData(SessionResult.ValidationFailed, 2)]
    [InlineData(SessionResult.VerdictUnclear, 2)]
    [InlineData(SessionResult.RevisionBrokeValidation, 2)]
    [InlineData(SessionResult.AbortedDirtyTree, 2)]
    [InlineData(SessionResult.Failed, 1)]
    public void MapToExitCode_buckets_outcomes(SessionResult reason, int expected)
        => Assert.Equal(expected, FlowRunner.MapToExitCode(reason));

    [Fact]
    public void Every_SessionResult_maps_to_a_valid_exit_code()
    {
        foreach (var r in Enum.GetValues<SessionResult>())
            Assert.Contains(FlowRunner.MapToExitCode(r), new[] { 0, 1, 2 });
    }
}
