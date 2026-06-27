using Xunit;

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

    // for i in 0..4: if (i < 3) { acc += i; acc += 1; } else { acc += 10; }  => 26
    static Block IfElseInLoop()
    {
        var acc = new Var<int>("acc");
        var i = new Var<int>("i");
        return new Block(
            new DefineNode(new Lit(0), acc),
            new LoopNode(new Lit(5), i, new Block(
                new IfElseNode(
                    new LessThanNode(new Ref(i), new Lit(3)),
                    new Block(
                        new AssignNode(acc, new AddNode(new Ref(acc), new Ref(i))),
                        new AssignNode(acc, new AddNode(new Ref(acc), new Lit(1)))),
                    new Block(
                        new AssignNode(acc, new AddNode(new Ref(acc), new Lit(10))))))),
            new ReturnNode(new Ref(acc)));
    }
}
