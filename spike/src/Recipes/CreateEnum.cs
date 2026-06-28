namespace Spike;

// A named set of constants. Shows the recipe-class pattern spans type kinds (record vs enum) with no
// shared machinery beyond IRecipe.
sealed class CreateEnum(string name, params string[] members) : IRecipe
{
    public string Name => "create-enum";

    public string Description => "A named enum of constants.";

    public TypeDecl Build() => new EnumNode(name, members);
}
