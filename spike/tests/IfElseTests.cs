using Xunit;
using static Spike.Recipe;

namespace Spike.Tests;

// Phase 2a: if/else — a bool condition, named then/else blocks, and a multi-statement block.
public class IfElseTests
{
    [Fact]
    public void IfElse_in_a_loop_branches_and_returns_26()
    {
        var code = Generator.Generate(IfElseInLoop());

        Assert.Equal(26, Runtime.CompileAndRun(code));
    }

    [Fact]
    public void Both_branches_and_the_multi_statement_then_block_are_emitted()
    {
        var code = Generator.Generate(IfElseInLoop());

        Assert.Contains("if (i < 3)", code);          // bool condition rendered
        Assert.Contains("else", code);                // named else block present
        Assert.Contains("acc = acc + i;", code);      // then block, statement 1
        Assert.Contains("acc = acc + 1;", code);      // then block, statement 2 (multi-statement)
        Assert.Contains("acc = acc + 10;", code);     // else block
    }

    [Fact]
    public void GreaterThan_renders_as_authored_not_a_flipped_lessthan()
    {
        var acc = new Var<int>("acc");
        var i = new Var<int>("i");
        Block recipe = [
            Define(acc, 0),
            Loop(i, 5, IfElse(i > 3, Assign(acc, acc + 1), Assign(acc, acc + 2))),
            Return(acc)];

        var code = Generator.Generate(recipe);

        Assert.Contains("if (i > 3)", code);
        Assert.DoesNotContain("3 < i", code);
    }

    [Fact]
    public void Eq_is_the_equality_path_and_renders_double_equals()
    {
        var acc = new Var<int>("acc");
        var i = new Var<int>("i");
        Block recipe = [
            Define(acc, 0),
            Loop(i, 5, IfElse(Eq(i, 3), Assign(acc, acc + 1), Assign(acc, acc + 2))),
            Return(acc)];

        var code = Generator.Generate(recipe);

        Assert.Contains("if (i == 3)", code);
    }

    // for i in 0..4: if (i < 3) { acc += i; acc += 1; } else { acc += 10; }  => 26
    static Block IfElseInLoop()
    {
        var acc = new Var<int>("acc");
        var i = new Var<int>("i");
        return [
            Define(acc, 0),
            Loop(i, 5,
                IfElse(i < 3,
                    [Assign(acc, acc + i), Assign(acc, acc + 1)],
                    Assign(acc, acc + 10))),
            Return(acc)];
    }
}
