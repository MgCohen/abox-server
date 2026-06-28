using Xunit;
using static Spike.Recipe;

namespace Spike.Tests;

// Pins the pre-source-gen behavior so the Step 2 refactor (generating the recipe nodes
// instead of hand-writing them) can be proven NOT to change the output.
public class GeneratorRegressionTests
{
    [Fact]
    public void LoopVarSum_generates_the_expected_source()
    {
        const string expected = """
            public static class ScriptData
            {
                public static int Run()
                {
                    int acc = 0;
                    for (int i = 0; i < 5; i++)
                    {
                        acc = acc + i;
                    }

                    return acc;
                }
            }
            """;

        var actual = Generator.Generate(LoopVarSum());

        Assert.Equal(Normalize(expected), Normalize(actual));
    }

    [Fact]
    public void LoopVarSum_compiles_and_returns_10()
    {
        var code = Generator.Generate(LoopVarSum());

        Assert.Equal(10, Runtime.CompileAndRun(code));
    }

    [Fact]
    public void A_second_recipe_shape_compiles_and_returns_its_value()
    {
        var x = new Var<int>("x");
        Block recipe = [Define(x, 7), Return(x)];

        var code = Generator.Generate(recipe);

        Assert.Equal(7, Runtime.CompileAndRun(code));
    }

    static Block LoopVarSum()
    {
        var acc = new Var<int>("acc");
        var i = new Var<int>("i");
        return [
            Define(acc, 0),
            Loop(i, 5, [
                Assign(acc, acc + i)]),
            Return(acc)];
    }

    static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd();
}
