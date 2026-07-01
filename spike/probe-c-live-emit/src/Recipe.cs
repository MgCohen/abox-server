namespace ProbeC;

// The recipe: a tiny typed tree, the single source of truth. PROBE C is about the emit GATE and
// DETACHMENT semantics, not the lowering — so the recipe is deliberately small. It models a record
// type plus a tiny derived helper, which the lowering turns into two .cs files.
//
// "Live" continuously lowers this in place; "emit" materializes it at a configured target. Edit the
// recipe in RecipeSource and re-run to see live update; never hand-edit the lowered output.
sealed record Recipe(string TypeName, IReadOnlyList<Field> Fields);

readonly record struct Field(string Name, string Type);

// The working recipe the author is "editing". A real authoring surface would feed this from the UI;
// here it is a checked-in literal plus an environment override (PROBE_C_RECIPE_VARIANT) so the proof
// script can flip the recipe between runs and observe detachment.
static class RecipeSource
{
    public static Recipe Current()
    {
        var variant = Environment.GetEnvironmentVariable("PROBE_C_RECIPE_VARIANT") ?? "v1";
        return variant switch
        {
            "v2" => new Recipe("Customer",
            [
                new Field("Id", "System.Guid"),
                new Field("Name", "string"),
                new Field("Email", "string"),       // added in v2
                new Field("CreatedAt", "System.DateTime"),
            ]),
            _ => new Recipe("Customer",
            [
                new Field("Id", "System.Guid"),
                new Field("Name", "string"),
                new Field("CreatedAt", "System.DateTime"),
            ]),
        };
    }
}
