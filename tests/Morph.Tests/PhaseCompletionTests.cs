using Morph;

namespace Morph.Tests;

public class PhaseCompletionTests
{
    [Fact]
    public void Begin_releases_the_prior_phase_and_advances_generation()
    {
        var completion = new PhaseCompletion();

        var (first, firstTask) = completion.Begin();
        Assert.False(firstTask.IsCompleted);

        var (second, _) = completion.Begin();

        Assert.True(firstTask.IsCompleted);
        Assert.NotEqual(first, second);
        Assert.False(completion.IsCurrent(first));
        Assert.True(completion.IsCurrent(second));
    }

    [Fact]
    public void CompleteIfCurrent_completes_only_the_matching_generation()
    {
        var completion = new PhaseCompletion();
        var (generation, task) = completion.Begin();

        completion.CompleteIfCurrent(generation - 1);
        Assert.False(task.IsCompleted);

        completion.CompleteIfCurrent(generation);
        Assert.True(task.IsCompleted);
    }

    [Fact]
    public void TryBeginWait_is_false_when_idle_and_guards_against_double_scheduling()
    {
        var completion = new PhaseCompletion();

        Assert.False(completion.TryBeginWait(out _));

        completion.Begin();
        Assert.True(completion.TryBeginWait(out _));
        Assert.False(completion.TryBeginWait(out _));

        completion.Begin();
        Assert.True(completion.TryBeginWait(out _));
    }

    [Fact]
    public void A_superseded_wait_does_not_complete_the_new_phase()
    {
        var completion = new PhaseCompletion();

        completion.Begin();
        completion.TryBeginWait(out var staleWait);

        var (current, currentTask) = completion.Begin();

        completion.CompleteIfCurrent(staleWait);
        Assert.False(currentTask.IsCompleted);

        completion.CompleteIfCurrent(current);
        Assert.True(currentTask.IsCompleted);
    }

    [Fact]
    public void Clear_stops_a_later_render_from_scheduling_another_wait()
    {
        var completion = new PhaseCompletion();

        var (generation, _) = completion.Begin();
        completion.CompleteIfCurrent(generation);
        completion.Clear();

        Assert.False(completion.TryBeginWait(out _));
    }
}
