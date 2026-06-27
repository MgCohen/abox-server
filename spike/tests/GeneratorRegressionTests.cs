using Xunit;

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

        var actual = Generator.Generate(Samples.LoopVarSum);

        Assert.Equal(Normalize(expected), Normalize(actual));
    }

    [Fact]
    public void LoopVarSum_compiles_and_returns_10()
    {
        var code = Generator.Generate(Samples.LoopVarSum);

        Assert.Equal(10, Runtime.CompileAndRun(code));
    }

    [Fact]
    public void A_second_recipe_shape_compiles_and_returns_its_value()
    {
        var recipe = new Block(
            new DefineNode("x", new Lit(7)),
            new ReturnNode(new Ref("x")));

        var code = Generator.Generate(recipe);

        Assert.Equal(7, Runtime.CompileAndRun(code));
    }

    static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd();
}
