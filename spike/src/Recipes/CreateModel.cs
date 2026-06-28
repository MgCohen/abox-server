namespace Spike;

// The north star's "Create Model": an entity as a positional record of typed fields. The canonical
// first catalog recipe — the entity name and fields are parameters (data), because the entity is a
// type being brought into existence, so there is no <T> to pass.
sealed class CreateModel(string entity, params Field[] fields) : IRecipe
{
    public string Name => "create-model";

    public string Description => "An entity model — a positional record of typed fields.";

    public TypeDecl Build() => new RecordNode(entity, fields);
}
