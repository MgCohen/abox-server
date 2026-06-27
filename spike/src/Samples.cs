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

    // loop + if/else: for i in 0..4, if (i < 3) acc += i then acc += 1, else acc += 10.
    //   i=0: +0,+1   i=1: +1,+1   i=2: +2,+1   i=3: +10   i=4: +10   => 26
    // Exercises a bool condition, named then/else blocks, and a multi-statement block.
    public static Block IfElseInLoop => new(
        new DefineNode(new Lit(0), "acc"),
        new LoopNode(new Lit(5), "i", new Block(
            new IfElseNode(
                new LessThanNode(new Ref("i"), new Lit(3)),
                new Block(
                    new AssignNode("acc", new AddNode(new Ref("acc"), new Ref("i"))),
                    new AssignNode("acc", new AddNode(new Ref("acc"), new Lit(1)))),
                new Block(
                    new AssignNode("acc", new AddNode(new Ref("acc"), new Lit(10))))))),
        new ReturnNode(new Ref("acc")));
}
