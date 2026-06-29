using Xunit;
using static Spike.Recipe;

namespace Spike.Tests;

// The method tier: a MethodNode joins the two tiers — its signature is declaration-tier, its body is
// the body tier. The hardcoded ScriptData shell becomes a recipe-composed class + method.
public class MethodTests
{
    [Fact]
    public void Method_body_is_the_body_tier_inside_a_real_type()
    {
        var acc = new Var<int>("acc");
        var i = new Var<int>("i");
        Block body = [Define(acc, 0), Loop(i, 5, Assign(acc, acc + i)), Return(acc)];

        var code = TypeEmitter.Emit(new ClassNode("Calculator",
            new MethodNode(TypeRef.Of<int>(), "Run", body)));

        Assert.Equal(10, (int)Runtime.Invoke(code, "Calculator", "Run"));
    }

    [Fact]
    public void Class_renders_fields_and_methods_together()
    {
        var code = TypeEmitter.Emit(new ClassNode("Mixed",
            new Field<int>("Seed"),
            new MethodNode(TypeRef.Of<int>(), "Run", [Return(0)])));

        Assert.Contains("public System.Int32 Seed { get; set; }", code);
        Assert.Contains("public System.Int32 Run()", code);
    }
}
