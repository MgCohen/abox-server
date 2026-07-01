namespace Spike;

// Shared sample recipes, so the tool and the regression tests compose against the same input.
static class Samples
{
    // loop + var + sum -> sum of the indices 0..4 == 10
    public static Block LoopVarSum => new(
        new DefineNode(new Lit(0), "acc"),
        new LoopNode(new Lit(5), "i", new Block(
            new AssignNode("acc", new AddNode(new Ref("acc"), new Ref("i"))))),
        new ReturnNode(new Ref("acc")));
}
