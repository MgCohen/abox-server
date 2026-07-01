using Probe.Domain;
using static Probe.Compose;

namespace Probe;

// THE SAME FEATURE, provider = Bucket — ONE token changed (store) plus the key it forces.
// Because the store is IStore<BucketKey,User>, `key` must be a BucketKey — `scope.Command.Email`
// would NOT compile here (that's the negative check in Program). The body is byte-identical to
// the Repo recipe: the divergence does not know about the provider.
public static class BucketRecipe
{
    // === AUTHORED (begin) ===
    public static Node AddPoints() =>
        new Feature<AddPointsCommand>(scope =>
            Mutate(store: Stores.BucketStore<User>(),
                   key:   new BucketKey(scope.Command.Region),
                   body:  user => user.AddPoints(scope.Command.Points)));
    // === AUTHORED (end) ===
}
