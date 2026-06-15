namespace ABox.Tests.Structure.Support;

// The csproj tally of one feature folder, split by role. The canonical slice (ADR 0010 D2) is exactly one
// implementation project (verbs as folders, Module folded in) and exactly one Contracts leaf.
internal sealed record FeatureProjects(int Implementation, int Contracts)
{
    public bool IsCanonical => Implementation == 1 && Contracts == 1;

    public override string ToString() => $"{Implementation} impl + {Contracts} contracts";
}
