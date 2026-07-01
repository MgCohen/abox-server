using Probe.Domain;

namespace Probe;

// THE SAME FEATURE, provider = Repo. Because via is a Repo (IStore<string,User>), the
// key MUST return a string — `c.Email` type-checks. The body is provider-agnostic.
// Everything between the AUTHORED markers is the measured surface.
public static class RepoRecipe
{
    // === AUTHORED (begin) ===
    public static Mutation AddPoints() => Feature.For<AddPointsCommand>().Mutate(
        via:  Stores.Repository<User>(),
        key:  c => c.Email,
        body: (user, c) => user.AddPoints(c.Points));
    // === AUTHORED (end) ===
}
