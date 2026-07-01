using Probe.Domain;
using static Probe.Compose;

namespace Probe;

// THE SAME FEATURE, provider = Repo. `new Feature<AddPointsCommand>(scope => …)` is the
// builder — it provides the scope. `Mutate(store, key, body)` is the action — no scope in its
// params; the leaves reach the command through the ambient `scope.Command`. Because the store
// is IStore<string,User>, `key` must be a string — `scope.Command.Email` type-checks.
// Everything between the AUTHORED markers is the measured surface.
public static class RepoRecipe
{
    // === AUTHORED (begin) ===
    public static Node AddPoints() =>
        new Feature<AddPointsCommand>(scope =>
            Mutate(store: Stores.Repository<User>(),
                   key:   scope.Command.Email,
                   body:  user => user.AddPoints(scope.Command.Points)));
    // === AUTHORED (end) ===
}
