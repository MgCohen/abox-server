using Probe.Domain;

namespace Probe;

// THE SAME FEATURE, provider = Bucket — ONE token changed (via) plus the key it forces.
// Because via is a Bucket (IStore<BucketKey,User>), the key MUST return a BucketKey —
// `c.Email` would NOT compile here (that's the negative check in Program). The body is
// byte-identical to the Repo recipe: the divergence does not know about the provider.
public static class BucketRecipe
{
    // === AUTHORED (begin) ===
    public static Mutation AddPoints() => Feature.For<AddPointsCommand>().Mutate(
        via:  Stores.BucketStore<User>(),
        key:  c => new BucketKey(c.Region),
        body: (user, c) => user.AddPoints(c.Points));
    // === AUTHORED (end) ===
}
