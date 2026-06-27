using Xunit;

namespace Spike.Tests;

// Phase 2a: if/else — a bool condition, named then/else blocks, and a multi-statement block.
public class IfElseTests
{
    [Fact]
    public void IfElse_in_a_loop_branches_and_returns_26()
    {
        var code = Generator.Generate(Samples.IfElseInLoop);

        Assert.Equal(26, Runtime.CompileAndRun(code));
    }

    [Fact]
    public void Both_branches_and_the_multi_statement_then_block_are_emitted()
    {
        var code = Generator.Generate(Samples.IfElseInLoop);

        Assert.Contains("if (i < 3)", code);          // bool condition rendered
        Assert.Contains("else", code);                // named else block present
        Assert.Contains("acc = acc + i;", code);      // then block, statement 1
        Assert.Contains("acc = acc + 1;", code);      // then block, statement 2 (multi-statement)
        Assert.Contains("acc = acc + 10;", code);     // else block
    }
}
